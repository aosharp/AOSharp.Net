using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AOSharp;
using AOSharp.Data;
using AOSharp.Models;
using Serilog;

namespace AOSharp.Services
{
    public class CompileProgressEventArgs : EventArgs
    {
        public string PluginName { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
        /// <summary>When set, the loader UI can mark this plugin compiled before the repo group finishes.</summary>
        public string DllPath { get; set; }
    }

    /// <summary>
    /// Returned by CompileAll. Contains new per-project entries and stub keys to remove from Config.
    /// </summary>
    public class CompileResult
    {
        public bool AllSucceeded { get; set; } = true;
        /// <summary>New per-project PluginModel entries keyed by config key.</summary>
        public Dictionary<string, PluginModel> NewEntries { get; } = new Dictionary<string, PluginModel>();
        /// <summary>Config keys of stub entries that were expanded and should be removed.</summary>
        public List<string> KeysToRemove { get; } = new List<string>();
    }

    public class RepoCompiler
    {
        /// <summary>How to pick among multiple local copies of the same AOSharp SDK assembly.</summary>
        private enum SdkReferencePreference
        {
            /// <summary>MSBuild should use outputs from the current SDK build (bin), not stale host copies.</summary>
            FreshBuild,
            /// <summary>Consumer plugins should bind to the live NativeHost bundle under Plugins\AOSharp.Bootstrap.</summary>
            LiveHost
        }

        /// <summary>Fallback when AOSharp.csproj cannot be found (keep in sync with Loader AOSharp.csproj).</summary>
        private const string LoaderTargetFrameworkFallback = "net10.0-windows";

        private static string _cachedLoaderTargetFramework;

        public event EventHandler<CompileProgressEventArgs> Progress;

        private void Report(string pluginName, string message, bool isError = false, string dllPath = null)
        {
            Log.Information($"[RepoCompiler] {pluginName}: {message}");
            Progress?.Invoke(this, new CompileProgressEventArgs
            {
                PluginName = pluginName,
                Message = message,
                IsError = isError,
                DllPath = !string.IsNullOrWhiteSpace(dllPath) && File.Exists(dllPath) ? dllPath : null
            });
        }

        // ── Git ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches from origin and returns whether an update is available plus both short commit hashes.
        /// Does not modify the working tree. Returns <c>(false, null, null)</c> if not yet cloned.
        /// </summary>
        public (bool hasUpdate, string localCommit, string remoteCommit) CheckForUpdate(string repoUrl)
        {
            var localPath = GetLocalRepoPath(repoUrl);
            if (!Directory.Exists(Path.Combine(localPath, ".git")))
                return (false, null, null);

            var localHead = RunGitSingleLine(localPath, "rev-parse --short HEAD");
            if (localHead == null) return (false, null, null);

            // Fetch from remote without touching the working tree
            RunGit(localPath, "fetch origin");

            var remoteHead = RunGitSingleLine(localPath, "rev-parse --short FETCH_HEAD");
            if (remoteHead == null) return (false, localHead, null);

            bool hasUpdate = !string.Equals(localHead, remoteHead, StringComparison.OrdinalIgnoreCase);
            return (hasUpdate, localHead, remoteHead);
        }

        /// <summary>
        /// Returns the short commit hash of the local HEAD without network access.
        /// Returns null if the repo has not been cloned yet.
        /// </summary>
        public string GetLocalCommit(string repoUrl)
        {
            var localPath = GetLocalRepoPath(repoUrl);
            if (!Directory.Exists(Path.Combine(localPath, ".git")))
                return null;
            return RunGitSingleLine(localPath, "rev-parse --short HEAD");
        }

        /// <summary>
        /// Runs a git command and returns the first non-empty output line, or null.
        /// </summary>
        private string RunGitSingleLine(string workingDir, string args)
        {
            var output = new List<string>();
            RunProcess("git", args, workingDir, output);
            return output.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        }

        /// <summary>
        /// Clones the repo if not present locally, or pulls latest changes if it is.
        /// </summary>
        public bool CloneOrPull(string repoUrl, string localPath)
        {
            if (Directory.Exists(Path.Combine(localPath, ".git")))
                return RunGit(localPath, "pull --ff-only");

            Directory.CreateDirectory(localPath);
            return RunGit(Directories.ReposDirPath, $"clone \"{repoUrl}\" \"{localPath}\"");
        }

        // ── Project discovery ──────────────────────────────────────────────────

        /// <summary>
        /// Returns all .csproj files found in the repo directory, along with library flag and optional Manifest.json metadata.
        /// </summary>
        public List<(string name, string csprojPath, bool isLibrary, string author, string description, List<string> dependencyRepoUrls)>
            DiscoverProjects(string localRepoPath)
        {
            var csprojs = Directory.GetFiles(localRepoPath, "*.csproj", SearchOption.AllDirectories);

            return csprojs
                .Select(p =>
                {
                    var manifest = PluginManifest.LoadForProject(p);
                    return (
                        Path.GetFileNameWithoutExtension(p),
                        p,
                        ReadIsLibrary(p),
                        manifest.Author,
                        manifest.Description,
                        manifest.Dependencies ?? new List<string>());
                })
                .OrderBy(p => p.Item1)
                .ToList();
        }

        /// <summary>Collects unique manifest dependency URLs declared anywhere in the repo.</summary>
        public static HashSet<string> CollectManifestDependencyUrls(string localRepoPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(localRepoPath) || !Directory.Exists(localRepoPath))
                return set;

            var csprojs = Directory.GetFiles(localRepoPath, "*.csproj", SearchOption.AllDirectories);
            foreach (var p in csprojs)
            {
                var manifest = PluginManifest.LoadForProject(p);
                if (manifest.Dependencies == null)
                    continue;
                foreach (var u in manifest.Dependencies)
                {
                    if (!string.IsNullOrWhiteSpace(u))
                        set.Add(u.Trim());
                }
            }

            return set;
        }

        /// <summary>
        /// Reads the &lt;AOSharpLibrary&gt; property from a .csproj file.
        /// Returns true only when the property is explicitly set to "true".
        /// </summary>
        public static bool ReadIsLibrary(string csprojPath)
        {
            try
            {
                var doc = XDocument.Load(csprojPath);
                var value = doc.Descendants("AOSharpLibrary").FirstOrDefault()?.Value;
                return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ── Reference overrides ────────────────────────────────────────────────

        /// <summary>
        /// True for SDK-style assemblies (<c>AOSharp.Common</c>, <c>AOSharp.Core</c>, …).
        /// Excludes the WPF loader host <c>AOSharp.dll</c> (stem <c>AOSharp</c> only), which would otherwise match <c>AOSharp.*</c> globs.
        /// </summary>
        private static bool IsAoSharpSdkAssemblyStem(string stem)
        {
            if (string.IsNullOrEmpty(stem))
                return false;
            if (string.Equals(stem, "AOSharp", StringComparison.OrdinalIgnoreCase))
                return false;
            return stem.StartsWith("AOSharp.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Finds AOSharp SDK assemblies next to the loader or under Plugins (library build output).
        /// Used so repo plugins that use direct assembly references resolve when HintPath or NuGet layout does not match the loader machine.
        /// </summary>
        public static List<(string packageId, string dllPath)> ResolveBundledAoSharpSdkDlls()
        {
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(Directories.CurrentDirectory) && Directory.Exists(Directories.CurrentDirectory))
                roots.Add(Directories.CurrentDirectory);
            if (!string.IsNullOrEmpty(Directories.PluginsDirPath) && Directory.Exists(Directories.PluginsDirPath))
                roots.Add(Directories.PluginsDirPath);

            var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                try
                {
                    foreach (var path in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
                    {
                        if (IsIntermediateAssemblyPath(path))
                            continue;
                        var id = Path.GetFileNameWithoutExtension(path);
                        if (!IsAoSharpSdkAssemblyStem(id))
                            continue;

                        var fullPath = Path.GetFullPath(path);
                        if (!byId.TryGetValue(id, out var existing) ||
                            SdkReferencePathScore(fullPath, SdkReferencePreference.FreshBuild) >
                            SdkReferencePathScore(existing, SdkReferencePreference.FreshBuild) ||
                            (SdkReferencePathScore(fullPath, SdkReferencePreference.FreshBuild) ==
                             SdkReferencePathScore(existing, SdkReferencePreference.FreshBuild) &&
                             File.GetLastWriteTimeUtc(fullPath) >= File.GetLastWriteTimeUtc(existing)))
                            byId[id] = fullPath;
                    }
                }
                catch
                {
                    // ignore IO errors under a single root
                }
            }

            TryFillSdkDllsFromDefaultSdkClone(byId);
            TryFillSdkDllsFromDevSourceTree(byId);

            return byId.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }

        /// <summary>
        /// Picks up AOSharp.*.dll built inside the default AOSharp.SDK clone (under repos) when Plugins is empty.
        /// </summary>
        private static void TryFillSdkDllsFromDefaultSdkClone(Dictionary<string, string> byId)
        {
            if (byId == null)
                return;

            var cloneRoot = GetLocalRepoPath(Config.AoSharpSdkRepoUrl);
            if (string.IsNullOrEmpty(cloneRoot) || !Directory.Exists(Path.Combine(cloneRoot, ".git")))
                return;

            try
            {
                foreach (var path in Directory.EnumerateFiles(cloneRoot, "AOSharp.*.dll", SearchOption.AllDirectories))
                {
                    if (IsIntermediateAssemblyPath(path))
                        continue;
                    var idx = path.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                        idx = path.IndexOf($"{Path.AltDirectorySeparatorChar}bin{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                        continue;

                    var id = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(id) || !id.StartsWith("AOSharp.", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!byId.TryGetValue(id, out var existing) ||
                        File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(existing))
                        byId[id] = Path.GetFullPath(path);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// When running from a dev layout (Loader\bin\...\net*-windows), pick up freshly built SDK DLLs from the sibling SDK folder.
        /// </summary>
        private static void TryFillSdkDllsFromDevSourceTree(Dictionary<string, string> byId)
        {
            if (byId == null)
                return;

            string exeDir = Directories.CurrentDirectory;
            if (string.IsNullOrEmpty(exeDir))
                return;

            string workspaceRoot;
            try
            {
                workspaceRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", ".."));
            }
            catch
            {
                return;
            }

            var sdkDir = Path.Combine(workspaceRoot, "SDK");
            if (!Directory.Exists(sdkDir))
                return;

            var tfm = GetLoaderTargetFramework();
            foreach (var proj in new[] { "AOSharp.Common", "AOSharp.Core", "AOSharp.Bootstrap" })
            {
                if (byId.ContainsKey(proj))
                    continue;

                foreach (var cfg in new[] { "Release", "Debug" })
                {
                    var p = Path.Combine(sdkDir, proj, "bin", cfg, tfm, proj + ".dll");
                    if (!File.Exists(p))
                        continue;
                    byId[proj] = Path.GetFullPath(p);
                    break;
                }
            }
        }

        private static string XmlEscapeHintPath(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// Adds SDK DLLs found next to the loader or under Plugins when the list does not already define that assembly.
        /// </summary>
        private static void AddBundledAoSharpSdkDllsWhereMissing(List<(string packageId, string dllPath)> list)
        {
            foreach (var pair in ResolveBundledAoSharpSdkDlls())
            {
                if (!list.Any(l => string.Equals(l.packageId, pair.packageId, StringComparison.OrdinalIgnoreCase)))
                    list.Add(pair);
            }
        }

        /// <summary>
        /// Inserts or replaces one library reference (by assembly / NuGet id).
        /// </summary>
        private static void UpsertCompiledLibrary(List<(string packageId, string dllPath)> list, (string packageId, string dllPath) entry)
        {
            if (string.IsNullOrWhiteSpace(entry.packageId) || string.IsNullOrWhiteSpace(entry.dllPath) ||
                !File.Exists(entry.dllPath) || IsIntermediateAssemblyPath(entry.dllPath))
                return;
            if (string.Equals(entry.packageId.Trim(), "AOSharp", StringComparison.OrdinalIgnoreCase))
                return;

            var ix = list.FindIndex(l => string.Equals(l.packageId, entry.packageId, StringComparison.OrdinalIgnoreCase));
            if (ix < 0)
                list.Add(entry);
            else if (SdkReferencePathScore(entry.dllPath, SdkReferencePreference.FreshBuild) >=
                     SdkReferencePathScore(list[ix].dllPath, SdkReferencePreference.FreshBuild))
                list[ix] = entry;
        }

        /// <summary>
        /// Applies locally compiled library plugins from the loader config (same role as NuGet overrides), replacing any prior path for the same id.
        /// </summary>
        private static void MergePrecompiledLibraries(List<(string packageId, string dllPath)> list,
            IEnumerable<(string packageId, string dllPath)> precompiled)
        {
            if (precompiled == null)
                return;
            foreach (var p in precompiled)
                UpsertCompiledLibrary(list, p);
        }

        /// <summary>Preferred build order when compiling all AOSharp.SDK library projects.</summary>
        private static readonly string[] AoSharpSdkLibraryBuildOrder =
        {
            "AOSharp.Bootstrap",
            "AOSharp.Common",
            "AOSharp.Core"
        };

        /// <summary>Registers only AOSharp SDK assembly outputs from a plugin build folder.</summary>
        private static void RegisterAoSharpSdkDllsFromOutputDir(
            List<(string packageId, string dllPath)> compiledLibraries,
            string outputDir)
        {
            if (compiledLibraries == null || string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
                return;

            foreach (var dll in Directory.GetFiles(outputDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var id = Path.GetFileNameWithoutExtension(dll);
                if (!IsAoSharpSdkAssemblyStem(id))
                    continue;
                UpsertCompiledLibrary(compiledLibraries, (id, dll));
            }
        }

        /// <summary>Builds Bootstrap → Common → Core in the AOSharp.SDK clone and registers DLL paths.</summary>
        private async Task<bool> BuildAoSharpSdkLibraryProjectsAsync(
            string logName,
            string sdkLocalPath,
            List<(string packageId, string dllPath)> compiledLibraries,
            bool pullFirst)
        {
            if (string.IsNullOrEmpty(sdkLocalPath))
                return false;

            if (pullFirst)
            {
                if (!await Task.Run(() => CloneOrPull(Config.AoSharpSdkRepoUrl, sdkLocalPath)))
                {
                    Report(logName, "Failed to clone/pull AOSharp.SDK.", isError: true);
                    return false;
                }
            }
            else if (!Directory.Exists(Path.Combine(sdkLocalPath, ".git")))
            {
                if (!await Task.Run(() => CloneOrPull(Config.AoSharpSdkRepoUrl, sdkLocalPath)))
                {
                    Report(logName, "Failed to clone AOSharp.SDK.", isError: true);
                    return false;
                }
            }

            await Task.Run(() => AlignCompiledRepoTargetFrameworks(sdkLocalPath, logName));

            var libraryProjects = DiscoverProjects(sdkLocalPath)
                .Where(p => p.isLibrary)
                .ToDictionary(p => p.name, p => p.csprojPath, StringComparer.OrdinalIgnoreCase);

            NormalizeCompiledSdkLibraryPaths(compiledLibraries, SdkReferencePreference.FreshBuild);

            foreach (var projName in AoSharpSdkLibraryBuildOrder)
            {
                if (!libraryProjects.TryGetValue(projName, out var csprojPath))
                    continue;

                await Task.Run(() => InjectReferenceOverrides(sdkLocalPath, compiledLibraries,
                    excludePackageIds: new[] { projName }, preferFreshSdkBuildOutputs: true));

                var copyToPlugins = !string.Equals(projName, "AOSharp.Bootstrap", StringComparison.OrdinalIgnoreCase);
                var primaryDll = await Task.Run(() => Build(csprojPath, projName, copyOutputToPlugins: copyToPlugins));
                if (primaryDll == null)
                {
                    Report(logName, $"Failed to build SDK library {projName}.", isError: true);
                    return false;
                }

                RegisterAoSharpSdkDllsFromOutputDir(compiledLibraries, Path.GetDirectoryName(primaryDll));
                NormalizeCompiledSdkLibraryPaths(compiledLibraries, SdkReferencePreference.FreshBuild);
                await Task.Run(ShutdownDotNetBuildServers);
            }

            SyncAoSharpSdkToBootstrapHostFolder(compiledLibraries);
            RegisterAoSharpSdkDllsFromOutputDir(compiledLibraries, GetBootstrapHostDirectory());
            NormalizeCompiledSdkLibraryPaths(compiledLibraries, SdkReferencePreference.LiveHost);
            ApplyPendingBootstrapSdkUpdates();

            foreach (var projName in AoSharpSdkLibraryBuildOrder)
            {
                var displayPath = ResolveSdkDisplayDllPath(projName, compiledLibraries);
                if (displayPath != null)
                    Report(projName, $"Ready: {displayPath}", dllPath: displayPath);
            }

            return compiledLibraries.Any(l =>
                string.Equals(l.packageId, "AOSharp.Core", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(l.dllPath));
        }

        /// <summary>
        /// NativeHost loads from Plugins\AOSharp.Bootstrap. Keep Bootstrap, Common, Core, and Reloaded deps in sync there.
        /// </summary>
        private static void SyncAoSharpSdkToBootstrapHostFolder(List<(string packageId, string dllPath)> compiledLibraries)
        {
            if (compiledLibraries == null)
                return;

            try
            {
                SyncAoSharpSdkToBootstrapHostFolderCore(compiledLibraries);
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "[RepoCompiler] Bootstrap host sync failed; check Plugins\\AOSharp.Bootstrap\\.update-staging.");
            }
        }

        private static void SyncAoSharpSdkToBootstrapHostFolderCore(List<(string packageId, string dllPath)> compiledLibraries)
        {
            // Build outputs stay in bin/Plugins until all SDK projects finish; then shut down MSBuild
            // nodes and copy once into the live NativeHost folder (Plugins\AOSharp.Bootstrap).
            ShutdownDotNetBuildServers();

            var hostDir = GetBootstrapHostDirectory();
            Directory.CreateDirectory(hostDir);

            foreach (var projName in AoSharpSdkLibraryBuildOrder)
            {
                var entry = compiledLibraries.FirstOrDefault(l =>
                    string.Equals(l.packageId, projName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(entry.dllPath) || !File.Exists(entry.dllPath))
                    continue;

                var buildDir = Path.GetDirectoryName(entry.dllPath);
                if (string.IsNullOrEmpty(buildDir))
                    continue;

                CopyTopLevelDllsToBootstrapHost(buildDir, hostDir, projName);

                foreach (var ext in new[] { ".deps.json", ".runtimeconfig.json" })
                {
                    var src = Path.Combine(buildDir, projName + ext);
                    if (!File.Exists(src))
                        continue;
                    CopyFileToBootstrapHostOrStage(src, Path.Combine(hostDir, projName + ext));
                }
            }

            EnsureBootstrapRuntimeConfig(hostDir, "AOSharp.Bootstrap");
        }

        /// <summary>Ensures AOSharp.Core exists in the library list before compiling consumer plugins.</summary>
        private async Task<bool> EnsureAoSharpSdkLibrariesAsync(
            string logName,
            List<(string packageId, string dllPath)> compiledLibraries,
            bool pullFirst)
        {
            if (compiledLibraries.Any(l =>
                    string.Equals(l.packageId, "AOSharp.Core", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(l.dllPath)))
                return true;

            var sdkPath = GetLocalRepoPath(Config.AoSharpSdkRepoUrl);
            if (!await BuildAoSharpSdkLibraryProjectsAsync(logName, sdkPath, compiledLibraries, pullFirst))
            {
                Report(logName, "AOSharp.Core.dll is missing — build AOSharp.SDK libraries in the loader first.", isError: true);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Writes AOSharpLoader.props / AOSharpLoader.targets into the repo root so MSBuild strips any
        /// stale <c>Reference</c> whose filename starts with <c>AOSharp.</c> (including strong-named includes)
        /// and replaces them with local DLL hint paths — same role as NuGet substitution.
        /// </summary>
        public void InjectReferenceOverrides(string localPath, IEnumerable<(string packageId, string dllPath)> localLibraries,
            IEnumerable<string> excludePackageIds = null, bool preferFreshSdkBuildOutputs = false)
        {
            const string msbuildNs = "http://schemas.microsoft.com/developer/msbuild/2003";

            var exclude = excludePackageIds == null
                ? null
                : new HashSet<string>(excludePackageIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                    StringComparer.OrdinalIgnoreCase);

            var referencePreference = preferFreshSdkBuildOutputs
                ? SdkReferencePreference.FreshBuild
                : SdkReferencePreference.LiveHost;
            var libraries = PreferSdkReferencePaths(
                    localLibraries ?? Enumerable.Empty<(string packageId, string dllPath)>(), referencePreference)
                .Where(t => exclude == null || !exclude.Contains(t.packageId))
                .ToList();

            Log.Information($"[RepoCompiler] AOSharpLoader injection at '{localPath}': {libraries.Count} SDK DLL(s).");
            foreach (var (id, path) in libraries)
                Log.Information($"[RepoCompiler]   {id} -> {path}");
            if (libraries.Count == 0)
                Log.Warning("[RepoCompiler] No AOSharp SDK DLL paths to inject — build AOSharp.SDK libraries in the loader first.");

            var propsSb = new StringBuilder();
            propsSb.AppendLine($"<Project xmlns=\"{msbuildNs}\">");
            if (libraries.Count > 0)
            {
                propsSb.AppendLine("  <ItemGroup>");
                foreach (var (packageId, dllPath) in libraries)
                {
                    var idEsc = SecurityElement.Escape(packageId) ?? packageId;
                    var pathEsc = XmlEscapeHintPath(dllPath);
                    propsSb.AppendLine($"    <PackageReference Update=\"{idEsc}\" ExcludeAssets=\"all\" PrivateAssets=\"all\" />");
                    propsSb.AppendLine($"    <Reference Remove=\"{idEsc}\" />");
                    propsSb.AppendLine($"    <Reference Include=\"{idEsc}\">");
                    propsSb.AppendLine($"      <HintPath>{pathEsc}</HintPath>");
                    propsSb.AppendLine("      <Private>false</Private>");
                    propsSb.AppendLine("    </Reference>");
                }

                propsSb.AppendLine("  </ItemGroup>");
            }

            propsSb.AppendLine("  <Import Project=\"AOSharpLoader.targets\" Condition=\"Exists('AOSharpLoader.targets')\" />");
            propsSb.AppendLine("</Project>");
            File.WriteAllText(Path.Combine(localPath, "AOSharpLoader.props"), propsSb.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var targetsSb = new StringBuilder();
            targetsSb.AppendLine($"<Project xmlns=\"{msbuildNs}\">");
            if (libraries.Count > 0)
            {
                targetsSb.AppendLine("  <ItemGroup>");
                foreach (var (packageId, dllPath) in libraries)
                {
                    var idEsc = SecurityElement.Escape(packageId) ?? packageId;
                    var pathEsc = XmlEscapeHintPath(dllPath);
                    targetsSb.AppendLine($"    <_AoSharpLoaderSdkDll Include=\"{idEsc}\">");
                    targetsSb.AppendLine($"      <DllHintPath>{pathEsc}</DllHintPath>");
                    targetsSb.AppendLine("    </_AoSharpLoaderSdkDll>");
                }

                targetsSb.AppendLine("  </ItemGroup>");

                targetsSb.AppendLine(
                    "  <Target Name=\"AOSharpLoader_ApplySdkReferences\" BeforeTargets=\"ResolveAssemblyReferences\">");
                targetsSb.AppendLine("    <ItemGroup>");
                foreach (var (packageId, _) in libraries)
                {
                    var idEsc = SecurityElement.Escape(packageId) ?? packageId;
                    targetsSb.AppendLine($"      <Reference Remove=\"{idEsc}\" />");
                }

                targetsSb.AppendLine(
                    "      <Reference Remove=\"@(Reference)\" Condition=\"'%(Filename)' != '' and $([System.String]::Copy('%(Filename)').StartsWith('AOSharp.'))\" />");
                targetsSb.AppendLine("    </ItemGroup>");
                targetsSb.AppendLine("    <ItemGroup>");
                targetsSb.AppendLine(
                    "      <Reference Include=\"%(_AoSharpLoaderSdkDll.Identity)\" Condition=\"Exists('%(_AoSharpLoaderSdkDll.DllHintPath)')\">");
                targetsSb.AppendLine("        <HintPath>%(_AoSharpLoaderSdkDll.DllHintPath)</HintPath>");
                targetsSb.AppendLine("        <Private>false</Private>");
                targetsSb.AppendLine("      </Reference>");
                targetsSb.AppendLine("    </ItemGroup>");
                targetsSb.AppendLine("  </Target>");
            }

            targetsSb.AppendLine("</Project>");
            File.WriteAllText(Path.Combine(localPath, "AOSharpLoader.targets"), targetsSb.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var dirBuildProps = Path.Combine(localPath, "Directory.Build.props");
            string importLine =
                $"  <Import Project=\"AOSharpLoader.props\" Condition=\"Exists('AOSharpLoader.props')\" />";

            if (File.Exists(dirBuildProps))
            {
                var content = File.ReadAllText(dirBuildProps);
                if (!content.Contains("AOSharpLoader.props", StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Replace("</Project>", importLine + Environment.NewLine + "</Project>");
                    File.WriteAllText(dirBuildProps, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
            else
            {
                File.WriteAllText(dirBuildProps,
                    $"<Project xmlns=\"{msbuildNs}\">{Environment.NewLine}{importLine}{Environment.NewLine}</Project>",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        // ── Target framework alignment (match loader) ─────────────────────────

        /// <summary>
        /// Reads &lt;TargetFramework&gt; from a nearby AOSharp.csproj (walks up from the app directory).
        /// Falls back to <see cref="LoaderTargetFrameworkFallback"/> when not found (e.g. shipped builds).
        /// </summary>
        public static string GetLoaderTargetFramework()
        {
            if (!string.IsNullOrEmpty(_cachedLoaderTargetFramework))
                return _cachedLoaderTargetFramework;

            _cachedLoaderTargetFramework = TryReadTargetFrameworkFromNearbyAoSharpCsproj() ?? LoaderTargetFrameworkFallback;
            return _cachedLoaderTargetFramework;
        }

        private static string TryReadTargetFrameworkFromNearbyAoSharpCsproj()
        {
            foreach (var start in GetLoaderTfmSearchRoots().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
                    continue;

                var dir = new DirectoryInfo(start);
                for (var depth = 0; depth < 14 && dir != null; depth++, dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, "AOSharp.csproj");
                    if (!File.Exists(candidate))
                        continue;

                    try
                    {
                        var doc = XDocument.Load(candidate);
                        var tfm = doc.Descendants()
                            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "TargetFramework", StringComparison.Ordinal))?.Value?.Trim();
                        if (!string.IsNullOrEmpty(tfm))
                            return tfm;
                    }
                    catch
                    {
                        // ignore and keep walking
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> GetLoaderTfmSearchRoots()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;
            var loc = SafeGetExecutingAssemblyDirectory();
            if (!string.IsNullOrEmpty(loc))
                yield return loc;
        }

        private static string SafeGetExecutingAssemblyDirectory()
        {
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(loc))
                    return null;
                return Path.GetDirectoryName(loc);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Bumps SDK-style TFMs in all .csproj and Directory.Build.props under the repo to match this loader.
        /// Leaves netstandard* and classic net4xx TFMs unchanged.
        /// </summary>
        public void AlignCompiledRepoTargetFrameworks(string localRepoPath, string pluginNameForLogging)
        {
            if (string.IsNullOrEmpty(localRepoPath) || !Directory.Exists(localRepoPath))
                return;

            var loaderTfm = GetLoaderTargetFramework();
            var any = false;

            foreach (var file in Directory.GetFiles(localRepoPath, "*.csproj", SearchOption.AllDirectories))
            {
                if (TryBumpTfmsInMsbuildXmlFile(file, loaderTfm))
                    any = true;
            }

            foreach (var file in Directory.GetFiles(localRepoPath, "Directory.Build.props", SearchOption.AllDirectories))
            {
                if (TryBumpTfmsInMsbuildXmlFile(file, loaderTfm))
                    any = true;
            }

            if (any)
                Report(pluginNameForLogging, $"Aligned repo target frameworks to {loaderTfm}.");
        }

        private static bool ShouldBumpTfmToken(string token)
        {
            var t = token?.Trim() ?? string.Empty;
            if (t.Length == 0)
                return false;
            if (t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
                return false;
            // Classic: net462, net472, net481 (no dot after major)
            if (Regex.IsMatch(t, @"^net\d{3,4}$", RegexOptions.IgnoreCase))
                return false;
            // .NET 5+ style: net6.0, net8.0-windows
            return Regex.IsMatch(t, @"^net\d+\.\d+", RegexOptions.IgnoreCase);
        }

        private static string MapTfmTokenToLoader(string token, string loaderTfm) =>
            ShouldBumpTfmToken(token) ? loaderTfm : token.Trim();

        private static string BumpTargetFrameworksElementValue(string value, string loaderTfm)
        {
            var parts = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => MapTfmTokenToLoader(p, loaderTfm))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return string.Join(";", parts);
        }

        private static bool TryBumpTfmsInMsbuildXmlFile(string path, string loaderTfm)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                return false;
            }

            var changed = false;
            foreach (var el in doc.Descendants().Where(e =>
                         string.Equals(e.Name.LocalName, "TargetFramework", StringComparison.Ordinal) ||
                         string.Equals(e.Name.LocalName, "TargetFrameworks", StringComparison.Ordinal)))
            {
                if (string.Equals(el.Name.LocalName, "TargetFramework", StringComparison.Ordinal))
                {
                    var v = el.Value.Trim();
                    if (ShouldBumpTfmToken(v) && !string.Equals(v, loaderTfm, StringComparison.OrdinalIgnoreCase))
                    {
                        el.Value = loaderTfm;
                        changed = true;
                    }
                }
                else
                {
                    var before = el.Value;
                    var after = BumpTargetFrameworksElementValue(before, loaderTfm);
                    if (!string.Equals(before.Trim(), after.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        el.Value = after;
                        changed = true;
                    }
                }
            }

            if (!changed)
                return false;

            try
            {
                doc.Save(path);
            }
            catch
            {
                return false;
            }

            return true;
        }

        // ── Solution / build configuration (valid Release|Platform for dotnet build) ──

        /// <summary>Preferred solution platforms when building Release (first match in .sln wins).</summary>
        private static readonly string[] SolutionPlatformPreference =
        {
            "x86", "Win32", "Any CPU", "AnyCPU", "x64"
        };

        /// <summary>Parses GlobalSection(SolutionConfigurationPlatforms) keys (left side of =).</summary>
        private static List<string> ParseSolutionConfigurationPlatformKeys(string slnPath)
        {
            var keys = new List<string>();
            try
            {
                var lines = File.ReadAllLines(slnPath);
                var inSection = false;
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (!inSection)
                    {
                        if (line.StartsWith("GlobalSection(SolutionConfigurationPlatforms)", StringComparison.OrdinalIgnoreCase))
                            inSection = true;
                        continue;
                    }

                    if (line.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (string.IsNullOrEmpty(line))
                        continue;

                    var eq = line.IndexOf('=');
                    if (eq < 0)
                        continue;

                    var left = line.Substring(0, eq).Trim();
                    if (left.Contains('|'))
                        keys.Add(left);
                }
            }
            catch
            {
                // ignored
            }

            return keys;
        }

        private static bool SolutionPlatformMatchesPreference(string preferenceToken, string solutionPlatform)
        {
            if (string.Equals(preferenceToken, solutionPlatform, StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(
                preferenceToken.Replace(" ", ""),
                solutionPlatform.Replace(" ", ""),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Picks a Release solution platform declared in the .sln (e.g. Any CPU when Release|x86 is absent).
        /// Returns null if the section cannot be read or has no Release row.
        /// </summary>
        private static string TryPickReleaseSolutionPlatform(string slnPath)
        {
            var keys = ParseSolutionConfigurationPlatformKeys(slnPath);
            if (keys.Count == 0)
                return null;

            var releasePlatforms = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var parts = key.Split(new[] { '|' }, 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                    continue;
                if (!parts[0].Equals("Release", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(parts[1]))
                    releasePlatforms.Add(parts[1]);
            }

            if (releasePlatforms.Count == 0)
                return null;

            foreach (var pref in SolutionPlatformPreference)
            {
                foreach (var plat in releasePlatforms)
                {
                    if (SolutionPlatformMatchesPreference(pref, plat))
                        return plat;
                }
            }

            return releasePlatforms[0];
        }

        /// <summary>Builds argv for <c>dotnet build</c>; uses ArgumentList-safe tokens (no shell).</summary>
        /// <remarks>Does not set OutputPath here — a global OutputPath breaks some projects' Copy targets (MSB3094).</remarks>
        /// <param name="slnReleasePlatform">From <see cref="TryPickReleaseSolutionPlatform"/> when building a .sln; otherwise null.</param>
        private static List<string> ComposeDotnetBuildArgumentList(string projectFile, string slnReleasePlatform)
        {
            var args = new List<string>
            {
                "build",
                projectFile,
                "-c",
                "Release",
                "-p:PlatformTarget=x86",
                "-v:m",
                "--nologo",
                "--disable-build-servers",
                "-p:UseSharedCompilation=false"
            };

            if (!string.IsNullOrEmpty(slnReleasePlatform))
            {
                var idx = args.IndexOf("Release");
                if (idx >= 0)
                {
                    // One argv; spaces in platform (e.g. "Any CPU") are OK with ArgumentList.
                    args.Insert(idx + 1, $"-p:Platform={slnReleasePlatform}");
                }
            }

            return args;
        }

        // ── Build ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the output folder for a given plugin/repo name: Plugins\{sanitizedName}\
        /// </summary>
        public static string GetPluginOutputPath(string pluginName)
        {
            var safe = string.Concat(pluginName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(Directories.PluginsDirPath, safe);
        }

        private const string BootstrapStagingDirName = ".update-staging";

        /// <summary>Runtime folder NativeHost loads (Plugins\AOSharp.Bootstrap).</summary>
        public static string GetBootstrapHostDirectory() => GetPluginOutputPath("AOSharp.Bootstrap");

        /// <summary>Best DLL path to show as compiled for an SDK project (host bundle, plugin folder, or last build output).</summary>
        private static string ResolveSdkDisplayDllPath(string projectName,
            List<(string packageId, string dllPath)> compiledLibraries)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                return null;

            if (string.Equals(projectName, "AOSharp.Bootstrap", StringComparison.OrdinalIgnoreCase))
            {
                var hostDll = Path.Combine(GetBootstrapHostDirectory(), projectName + ".dll");
                if (File.Exists(hostDll))
                    return hostDll;
            }

            var pluginDll = Path.Combine(GetPluginOutputPath(projectName), projectName + ".dll");
            if (File.Exists(pluginDll))
                return pluginDll;

            var entry = compiledLibraries?.FirstOrDefault(l =>
                string.Equals(l.packageId, projectName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(entry?.dllPath) && File.Exists(entry.Value.dllPath))
                return entry.Value.dllPath;

            return null;
        }

        private static string GetBootstrapStagingDirectory() =>
            Path.Combine(GetBootstrapHostDirectory(), BootstrapStagingDirName);

        /// <summary>SDK DLL in the live NativeHost host folder (Plugins\AOSharp.Bootstrap\*.dll).</summary>
        private static bool IsBootstrapHostRuntimeFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                var hostDir = Path.GetFullPath(GetBootstrapHostDirectory());
                var fileDir = Path.GetFullPath(Path.GetDirectoryName(filePath) ?? string.Empty);
                if (!string.Equals(fileDir, hostDir, StringComparison.OrdinalIgnoreCase))
                    return false;
                return IsAoSharpSdkAssemblyStem(Path.GetFileNameWithoutExtension(filePath));
            }
            catch
            {
                return false;
            }
        }

        private static int SdkReferencePathScore(string path, SdkReferencePreference preference)
        {
            if (string.IsNullOrWhiteSpace(path))
                return 0;

            var inBin = path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                StringComparison.OrdinalIgnoreCase) ||
                        path.Contains($"{Path.AltDirectorySeparatorChar}bin{Path.AltDirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase);
            var inPluginProjectDir = path.Contains(
                $"{Path.DirectorySeparatorChar}Plugins{Path.DirectorySeparatorChar}AOSharp.",
                StringComparison.OrdinalIgnoreCase);
            var inLiveHost = IsBootstrapHostRuntimeFile(path);

            if (preference == SdkReferencePreference.FreshBuild)
            {
                if (inBin)
                    return 5;
                if (inPluginProjectDir && !inLiveHost)
                    return 4;
                if (inLiveHost)
                    return 1;
                return 2;
            }

            if (inLiveHost)
                return 5;
            if (inPluginProjectDir)
                return 4;
            if (inBin)
                return 3;
            return 1;
        }

        private static List<(string packageId, string dllPath)> PreferSdkReferencePaths(
            IEnumerable<(string packageId, string dllPath)> libraries,
            SdkReferencePreference preference = SdkReferencePreference.LiveHost)
        {
            var byId = new Dictionary<string, (string packageId, string dllPath)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in libraries)
            {
                if (string.IsNullOrWhiteSpace(entry.packageId) || string.IsNullOrWhiteSpace(entry.dllPath))
                    continue;
                if (!File.Exists(entry.dllPath) || IsIntermediateAssemblyPath(entry.dllPath))
                    continue;

                var normalized = (packageId: entry.packageId.Trim(), dllPath: Path.GetFullPath(entry.dllPath.Trim()));
                if (!IsAoSharpSdkAssemblyStem(normalized.packageId) ||
                    SdkReferencePathScore(normalized.dllPath, preference) <= 0)
                    continue;

                if (!byId.TryGetValue(normalized.packageId, out var existing) ||
                    SdkReferencePathScore(normalized.dllPath, preference) >
                    SdkReferencePathScore(existing.dllPath, preference))
                    byId[normalized.packageId] = normalized;
            }

            return byId.Values.OrderBy(t => t.packageId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void NormalizeCompiledSdkLibraryPaths(List<(string packageId, string dllPath)> list,
            SdkReferencePreference preference = SdkReferencePreference.LiveHost)
        {
            if (list == null || list.Count == 0)
                return;

            var normalized = PreferSdkReferencePaths(list, preference);
            list.Clear();
            list.AddRange(normalized);
        }

        /// <summary>True when a compile staged SDK files under Plugins\AOSharp.Bootstrap\.update-staging.</summary>
        public static bool HasPendingBootstrapSdkUpdates()
        {
            var stagingDir = GetBootstrapStagingDirectory();
            if (!Directory.Exists(stagingDir))
                return false;

            try
            {
                return Directory.EnumerateFileSystemEntries(stagingDir).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies staged SDK binaries into Plugins\AOSharp.Bootstrap when they are no longer locked.
        /// </summary>
        public static bool ApplyPendingBootstrapSdkUpdates()
        {
            var hostDir = GetBootstrapHostDirectory();
            var stagingDir = GetBootstrapStagingDirectory();
            if (!Directory.Exists(stagingDir))
                return true;

            Directory.CreateDirectory(hostDir);
            var pending = false;

            foreach (var stagedPath in Directory.EnumerateFiles(stagingDir))
            {
                var dest = Path.Combine(hostDir, Path.GetFileName(stagedPath));
                if (TryCopyFileWithRetry(stagedPath, dest))
                {
                    try
                    {
                        File.Delete(stagedPath);
                    }
                    catch
                    {
                        pending = true;
                    }
                }
                else
                {
                    pending = true;
                }
            }

            TryDeleteDirectoryIfEmpty(stagingDir);
            return !pending;
        }

        private static void TryDeleteDirectoryIfEmpty(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath) && !Directory.EnumerateFileSystemEntries(directoryPath).Any())
                    Directory.Delete(directoryPath);
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsFileSharingError(Exception ex)
        {
            if (ex is not IOException ioEx)
                return ex is UnauthorizedAccessException;

            if (ioEx.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
                ioEx.Message.Contains("because it is being used by another process", StringComparison.OrdinalIgnoreCase))
                return true;

            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            var hresult = ioEx.HResult;
            return hresult == sharingViolation || hresult == lockViolation ||
                   (hresult & 0xFFFF) == 0x20 || (hresult & 0xFFFF) == 0x21;
        }

        private static bool TryCopyFileWithRetry(string sourceFile, string destFile, int maxAttempts = 6)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile) ?? destFile);

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    File.Copy(sourceFile, destFile, overwrite: true);
                    return true;
                }
                catch (Exception ex) when (IsFileSharingError(ex))
                {
                    if (attempt < maxAttempts - 1)
                        Thread.Sleep(150 * (attempt + 1));
                }
            }

            return false;
        }

        private static void CopyFileWithRetryOrThrow(string sourceFile, string destFile)
        {
            if (!TryCopyFileWithRetry(sourceFile, destFile))
                throw new IOException(
                    $"The process cannot access the file '{destFile}' because it is being used by another process.");
        }

        private static void ShutdownDotNetBuildServers()
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("build-server");
                psi.ArgumentList.Add("shutdown");

                using var process = Process.Start(psi);
                process?.WaitForExit();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[RepoCompiler] dotnet build-server shutdown failed (non-fatal).");
            }
        }

        private static void CopyFileToBootstrapHostOrStage(string sourceFile, string destFile)
        {
            if (TryCopyFileWithRetry(sourceFile, destFile))
                return;

            var stagingDir = GetBootstrapStagingDirectory();
            Directory.CreateDirectory(stagingDir);
            var stagedPath = Path.Combine(stagingDir, Path.GetFileName(destFile));
            File.Copy(sourceFile, stagedPath, overwrite: true);
            Log.Warning(
                "[RepoCompiler] Could not update live {File} — copied to Plugins\\AOSharp.Bootstrap\\.update-staging. Restart the loader to apply.",
                Path.GetFileName(destFile));
        }

        private static void CopyTopLevelDllsToBootstrapHost(string buildOutDir, string hostDir, string outputName)
        {
            if (string.IsNullOrEmpty(buildOutDir) || !Directory.Exists(buildOutDir))
                return;

            foreach (var dll in Directory.GetFiles(buildOutDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var stem = Path.GetFileNameWithoutExtension(dll);
                if (IsAoSharpSdkAssemblyStem(stem) &&
                    !string.Equals(stem, outputName, StringComparison.OrdinalIgnoreCase))
                    continue;

                CopyFileToBootstrapHostOrStage(dll, Path.Combine(hostDir, Path.GetFileName(dll)));
            }
        }

        private static void TryDeleteFileQuiet(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Builds a project to its default output folder, then copies DLLs into Plugins\{outputName}\.
        /// Returns the path to the primary output DLL under Plugins, or null on failure.
        /// </summary>
        public string Build(string projectFile, string outputName, bool copyOutputToPlugins = true)
        {
            Report(outputName, $"Building {Path.GetFileName(projectFile)}...");

            var pluginOutputDir = GetPluginOutputPath(outputName);
            if (copyOutputToPlugins)
                Directory.CreateDirectory(pluginOutputDir);

            string slnReleasePlatform = null;
            if (projectFile.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                slnReleasePlatform = TryPickReleaseSolutionPlatform(projectFile);
                if (!string.IsNullOrEmpty(slnReleasePlatform) &&
                    !slnReleasePlatform.Equals("x86", StringComparison.OrdinalIgnoreCase) &&
                    !slnReleasePlatform.Equals("Win32", StringComparison.OrdinalIgnoreCase))
                {
                    Report(outputName,
                        $"Solution has no Release|x86; building Release|{slnReleasePlatform} with PlatformTarget=x86 for 32-bit output.");
                }
            }

            var outputLines = new List<string>();
            var argv = ComposeDotnetBuildArgumentList(projectFile, slnReleasePlatform);
            bool success = RunProcessWithArgumentList("dotnet", argv, Path.GetDirectoryName(projectFile), outputLines);

            if (!success)
            {
                Report(outputName, "Build failed.", isError: true);
                foreach (var line in outputLines.Where(l => l.Contains("error")))
                    Report(outputName, line, isError: true);
                return null;
            }

            var builtDll = FindBuiltDll(projectFile, outputLines);
            if (builtDll == null)
            {
                Report(outputName, "Build succeeded but could not locate primary output DLL.", isError: true);
                return null;
            }

            if (copyOutputToPlugins)
            {
                try
                {
                    CopyTopLevelDllsFromBuildOutput(Path.GetDirectoryName(builtDll), pluginOutputDir, outputName);
                }
                catch (Exception ex)
                {
                    Report(outputName, $"Build output copy failed: {ex.Message}", isError: true);
                    return null;
                }

                var deployed = Path.Combine(pluginOutputDir, Path.GetFileName(builtDll));
                var resultPath = File.Exists(deployed) ? deployed : builtDll;
                Report(outputName, $"Built: {resultPath}", dllPath: resultPath);
                return resultPath;
            }

            Report(outputName, $"Built: {builtDll}", dllPath: builtDll);
            return builtDll;
        }

        /// <summary>
        /// Copies runtime artifacts from the project's build output into Plugins\{outputName}\.
        /// Skips other AOSharp.SDK DLLs so a Common build does not drop Core into Plugins\Common.
        /// Includes runtimeconfig/deps for NativeHost (AOSharp.Bootstrap).
        /// </summary>
        private static void CopyTopLevelDllsFromBuildOutput(string buildOutDir, string pluginOutputDir, string outputName)
        {
            if (string.IsNullOrEmpty(buildOutDir) || !Directory.Exists(buildOutDir))
                return;

            Directory.CreateDirectory(pluginOutputDir);
            foreach (var dll in Directory.GetFiles(buildOutDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var stem = Path.GetFileNameWithoutExtension(dll);
                if (IsAoSharpSdkAssemblyStem(stem) &&
                    !string.Equals(stem, outputName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dest = Path.Combine(pluginOutputDir, Path.GetFileName(dll));
                CopyFileWithRetryOrThrow(dll, dest);
            }

            if (string.IsNullOrEmpty(outputName))
                return;

            foreach (var ext in new[] { ".runtimeconfig.json", ".deps.json" })
            {
                var src = Path.Combine(buildOutDir, outputName + ext);
                if (!File.Exists(src))
                    continue;
                CopyFileWithRetryOrThrow(src, Path.Combine(pluginOutputDir, outputName + ext));
            }
        }

        /// <summary>
        /// hostfxr requires AOSharp.Bootstrap.runtimeconfig.json; library projects omit it unless GenerateRuntimeConfigurationFiles is set.
        /// </summary>
        private static void EnsureBootstrapRuntimeConfig(string pluginOutputDir, string assemblyName)
        {
            var path = Path.Combine(pluginOutputDir, assemblyName + ".runtimeconfig.json");
            if (File.Exists(path))
                return;

            var loaderTfm = GetLoaderTargetFramework();
            var m = Regex.Match(loaderTfm ?? "", @"net(\d+)\.(\d+)", RegexOptions.IgnoreCase);
            var major = m.Success ? m.Groups[1].Value : "10";
            var minor = m.Success ? m.Groups[2].Value : "0";

            var json =
                "{\r\n" +
                "  \"runtimeOptions\": {\r\n" +
                $"    \"tfm\": \"net{major}.{minor}\",\r\n" +
                "    \"rollForward\": \"LatestMinor\",\r\n" +
                "    \"framework\": {\r\n" +
                "      \"name\": \"Microsoft.NETCore.App\",\r\n" +
                $"      \"version\": \"{major}.{minor}.0\"\r\n" +
                "    },\r\n" +
                "    \"configProperties\": {\r\n" +
                "      \"System.Reflection.Metadata.MetadataUpdater.IsSupported\": false,\r\n" +
                "      \"System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization\": false\r\n" +
                "    }\r\n" +
                "  }\r\n" +
                "}\r\n";

            Directory.CreateDirectory(pluginOutputDir);
            File.WriteAllText(path, json);
        }

        private static string TryProbeDefaultBuildOutputDll(string csprojPath, string assemblyName)
        {
            var projDir = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrEmpty(projDir) || string.IsNullOrEmpty(assemblyName))
                return null;

            var binRelease = Path.Combine(projDir, "bin", "Release");
            if (!Directory.Exists(binRelease))
                return null;

            try
            {
                return Directory.EnumerateFiles(binRelease, assemblyName + ".dll", SearchOption.AllDirectories)
                    .FirstOrDefault(p => !IsIntermediateAssemblyPath(p));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True for MSBuild intermediate outputs (obj, ref, refint) that must not be used as injected SDK references.
        /// </summary>
        private static bool IsIntermediateAssemblyPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            foreach (var segment in new[] { "obj", "ref", "refint" })
            {
                if (PathContainsDirectorySegment(filePath, segment))
                    return true;
            }

            return false;
        }

        private static bool PathContainsDirectorySegment(string filePath, string segment)
        {
            var sep = Path.DirectorySeparatorChar;
            var alt = Path.AltDirectorySeparatorChar;
            return filePath.Contains($"{sep}{segment}{sep}", StringComparison.OrdinalIgnoreCase) ||
                   filePath.Contains($"{alt}{segment}{alt}", StringComparison.OrdinalIgnoreCase);
        }

        private string FindBuiltDll(string projectFile, List<string> buildOutput)
        {
            var projectStem = Path.GetFileNameWithoutExtension(projectFile);
            var arrowPattern = new Regex(@"->\s*(.+\.dll)", RegexOptions.IgnoreCase);
            var raw = new List<string>();
            foreach (var line in buildOutput)
            {
                foreach (Match m in arrowPattern.Matches(line))
                {
                    var path = m.Groups[1].Value.Trim();
                    if (File.Exists(path))
                        raw.Add(path);
                }
            }

            var candidates = raw.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var exact = candidates.FirstOrDefault(p =>
                Path.GetFileNameWithoutExtension(p).Equals(projectStem, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var projDir = Path.GetDirectoryName(projectFile);
            if (!string.IsNullOrEmpty(projDir))
            {
                var underProj = candidates.LastOrDefault(p =>
                    p.StartsWith(projDir, StringComparison.OrdinalIgnoreCase) && !IsIntermediateAssemblyPath(p));
                if (underProj != null)
                    return underProj;
            }

            return candidates.LastOrDefault(p => !IsIntermediateAssemblyPath(p))
                   ?? TryProbeDefaultBuildOutputDll(projectFile, projectStem);
        }

        /// <summary>Re-reads Manifest.json from disk into the plugin (repo projects with a .csproj path only).</summary>
        public static void ApplyManifestToPlugin(PluginModel plugin, string localRepoPath)
        {
            if (plugin == null || plugin.PluginType != PluginType.Repo || string.IsNullOrEmpty(localRepoPath) ||
                !Directory.Exists(localRepoPath) || string.IsNullOrEmpty(plugin.ProjectFilePath))
                return;

            var m = PluginManifest.LoadForProject(plugin.ProjectFilePath);
            plugin.Author = m.Author;
            plugin.Description = m.Description;
            plugin.DependencyRepoUrls = m.Dependencies != null ? new List<string>(m.Dependencies) : new List<string>();
        }

        private static IEnumerable<string> GetManifestDependencyUrlsForPlugin(PluginModel plugin, string localRepoPath)
        {
            if (string.IsNullOrEmpty(localRepoPath) || !Directory.Exists(localRepoPath))
                yield break;

            if (plugin.IsStub)
            {
                foreach (var u in CollectManifestDependencyUrls(localRepoPath))
                    yield return u;
                yield break;
            }

            if (string.IsNullOrEmpty(plugin.ProjectFilePath))
                yield break;

            var m = PluginManifest.LoadForProject(plugin.ProjectFilePath);
            if (m.Dependencies == null)
                yield break;

            foreach (var u in m.Dependencies)
            {
                if (!string.IsNullOrWhiteSpace(u))
                    yield return u.Trim();
            }
        }

        private static string GetManifestDependencyOutputFolderName(string depRepoUrl)
        {
            var h = Utils.HashFromString(depRepoUrl ?? string.Empty);
            return string.Concat(("manifest_dep_" + h).Split(Path.GetInvalidFileNameChars()));
        }

        private async Task<bool> BuildManifestDependencyReposAsync(
            string logName,
            string depRepoUrl,
            string skipEqualToRootRepoUrl,
            List<(string packageId, string dllPath)> compiledLibraries,
            HashSet<string> completed,
            HashSet<string> inProgress,
            bool pullFirst)
        {
            if (!string.IsNullOrEmpty(skipEqualToRootRepoUrl) &&
                string.Equals(depRepoUrl, skipEqualToRootRepoUrl, StringComparison.OrdinalIgnoreCase))
                return true;

            if (completed.Contains(depRepoUrl))
                return true;

            if (!inProgress.Add(depRepoUrl))
            {
                Report(logName, $"Manifest dependency cycle at '{depRepoUrl}'.", isError: true);
                return false;
            }

            try
            {
                var depLocal = GetLocalRepoPath(depRepoUrl);
                if (pullFirst)
                {
                    if (!await Task.Run(() => CloneOrPull(depRepoUrl, depLocal)))
                    {
                        Report(logName, $"Failed to clone/pull manifest dependency: {depRepoUrl}", isError: true);
                        return false;
                    }
                }
                else if (!Directory.Exists(Path.Combine(depLocal, ".git")))
                {
                    if (!await Task.Run(() => CloneOrPull(depRepoUrl, depLocal)))
                    {
                        Report(logName, $"Failed to clone manifest dependency: {depRepoUrl}", isError: true);
                        return false;
                    }
                }

                foreach (var nested in CollectManifestDependencyUrls(depLocal))
                {
                    if (!await BuildManifestDependencyReposAsync(
                            logName, nested, skipEqualToRootRepoUrl, compiledLibraries, completed, inProgress, pullFirst))
                        return false;
                }

                if (string.Equals(depRepoUrl, Config.AoSharpSdkRepoUrl, StringComparison.OrdinalIgnoreCase))
                {
                    if (!await BuildAoSharpSdkLibraryProjectsAsync(logName, depLocal, compiledLibraries, pullFirst: false))
                        return false;
                }
                else
                {
                    await Task.Run(() => AlignCompiledRepoTargetFrameworks(depLocal, logName));
                    await Task.Run(() => InjectReferenceOverrides(depLocal, compiledLibraries));

                    var depTarget = FindBuildTarget(depLocal);
                    if (depTarget == null)
                    {
                        Report(logName, $"Manifest dependency has no .csproj: {depRepoUrl}", isError: true);
                        return false;
                    }

                    var outFolder = GetManifestDependencyOutputFolderName(depRepoUrl);
                    var primaryDll = await Task.Run(() => Build(depTarget, outFolder));
                    if (primaryDll == null)
                        return false;

                    RegisterAoSharpSdkDllsFromOutputDir(compiledLibraries, GetPluginOutputPath(outFolder));
                }

                completed.Add(depRepoUrl);
                return true;
            }
            finally
            {
                inProgress.Remove(depRepoUrl);
            }
        }

        private async Task<bool> BuildAllManifestDependenciesAsync(
            string logName,
            IEnumerable<string> topLevelDependencyUrls,
            string skipEqualToRootRepoUrl,
            List<(string packageId, string dllPath)> compiledLibraries,
            bool pullFirst)
        {
            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var url in topLevelDependencyUrls.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                var trimmed = url.Trim();
                if (!await BuildManifestDependencyReposAsync(
                        logName, trimmed, skipEqualToRootRepoUrl, compiledLibraries, completed, inProgress, pullFirst))
                    return false;
            }

            return true;
        }

        // ── CompileAll ─────────────────────────────────────────────────────────

        /// <summary>
        /// Compiles all repo plugins. Stubs are expanded to per-project entries.
        /// <paramref name="onGroupComplete"/> is invoked after each repo group finishes
        /// so the caller can apply partial results immediately rather than waiting for all groups.
        /// <paramref name="precompiledLibraries"/> should be the loader's compiled library plugins (e.g. SDK DLLs from <see cref="Config.GetCompiledLibraryPaths"/>); they override disk discovery like NuGet substitution.
        /// Returns the accumulated CompileResult for all groups.
        /// </summary>
        public async Task<CompileResult> CompileAll(
            IEnumerable<KeyValuePair<string, PluginModel>> plugins,
            bool pullFirst = true,
            Action<CompileResult> onGroupComplete = null,
            IEnumerable<(string packageId, string dllPath)> precompiledLibraries = null)
        {
            var result = new CompileResult();
            var compiledLibraries = new List<(string packageId, string dllPath)>();
            AddBundledAoSharpSdkDllsWhereMissing(compiledLibraries);
            MergePrecompiledLibraries(compiledLibraries, precompiledLibraries);
            NormalizeCompiledSdkLibraryPaths(compiledLibraries);

            // Group by repo URL; compile libraries first
            var groups = plugins
                .Where(p => p.Value.PluginType == PluginType.Repo)
                .GroupBy(p => p.Value.RepoUrl)
                .OrderByDescending(g => g.Any(p => p.Value.IsLibrary))
                .ThenBy(g => g.Key)
                .ToList();

            foreach (var group in groups)
            {
                var repoUrl = group.Key;
                var localPath = GetLocalRepoPath(repoUrl);
                var repoEntries = group.ToList();

                // Identify whether we have stubs or already-expanded project entries
                var stubs = repoEntries.Where(p => p.Value.IsStub).ToList();
                var projectEntries = repoEntries.Where(p => !p.Value.IsStub).ToList();

                // Inherit repo-level settings from the first stub (or first entry)
                var representative = (stubs.Any() ? stubs[0] : projectEntries[0]).Value;
                var repoName = representative.Name;

                // Collect this group's mutations separately so we can fire the callback
                var groupResult = new CompileResult();

                Report(repoName, $"Processing {repoUrl}");

                // Clone or pull
                if (pullFirst)
                {
                    bool pulled = await Task.Run(() => CloneOrPull(repoUrl, localPath));
                    if (!pulled)
                    {
                        Report(repoName, "Failed to clone/pull.", isError: true);
                        groupResult.AllSucceeded = false;
                        MergeAndNotify(result, groupResult, onGroupComplete);
                        continue;
                    }
                }
                else if (!Directory.Exists(Path.Combine(localPath, ".git")))
                {
                    bool cloned = await Task.Run(() => CloneOrPull(repoUrl, localPath));
                    if (!cloned)
                    {
                        Report(repoName, "Failed to clone.", isError: true);
                        groupResult.AllSucceeded = false;
                        MergeAndNotify(result, groupResult, onGroupComplete);
                        continue;
                    }
                }

                var manifestDepUrls = CollectManifestDependencyUrls(localPath)
                    .Where(u => !string.Equals(u, repoUrl, StringComparison.OrdinalIgnoreCase));
                if (!await BuildAllManifestDependenciesAsync(repoName, manifestDepUrls, repoUrl, compiledLibraries, pullFirst))
                {
                    Report(repoName, "Manifest dependency build failed.", isError: true);
                    groupResult.AllSucceeded = false;
                    MergeAndNotify(result, groupResult, onGroupComplete);
                    continue;
                }

                // Re-apply loader compiled libraries so SDK paths always win over discovery / other outputs (same as NuGet overrides).
                MergePrecompiledLibraries(compiledLibraries, precompiledLibraries);
                NormalizeCompiledSdkLibraryPaths(compiledLibraries);

                var isSdkRepo = string.Equals(repoUrl, Config.AoSharpSdkRepoUrl, StringComparison.OrdinalIgnoreCase);

                if (!isSdkRepo)
                {
                    if (!await EnsureAoSharpSdkLibrariesAsync(repoName, compiledLibraries, pullFirst))
                    {
                        groupResult.AllSucceeded = false;
                        MergeAndNotify(result, groupResult, onGroupComplete);
                        continue;
                    }
                }

                // Inject reference overrides using everything compiled so far
                await Task.Run(() => InjectReferenceOverrides(localPath, compiledLibraries));

                await Task.Run(() => AlignCompiledRepoTargetFrameworks(localPath, repoName));

                if (isSdkRepo)
                {
                    if (!await BuildAoSharpSdkLibraryProjectsAsync(repoName, localPath, compiledLibraries, pullFirst: false))
                    {
                        groupResult.AllSucceeded = false;
                        MergeAndNotify(result, groupResult, onGroupComplete);
                        continue;
                    }
                }
                else
                {
                    var buildTarget = FindBuildTarget(localPath);
                    if (buildTarget == null)
                    {
                        Report(repoName, "No .csproj found.", isError: true);
                        groupResult.AllSucceeded = false;
                        MergeAndNotify(result, groupResult, onGroupComplete);
                        continue;
                    }

                    var primaryDll = await Task.Run(() => Build(buildTarget, repoName));
                    if (primaryDll == null)
                    {
                        groupResult.AllSucceeded = false;
                        MergeAndNotify(result, groupResult, onGroupComplete);
                        continue;
                    }

                    RegisterAoSharpSdkDllsFromOutputDir(compiledLibraries, GetPluginOutputPath(repoName));
                }

                // Discover per-project entries
                var discoveredProjects = await Task.Run(() => DiscoverProjects(localPath));

                if (stubs.Any())
                {
                    // Replace stubs with per-project entries
                    foreach (var stub in stubs)
                        groupResult.KeysToRemove.Add(stub.Key);

                    foreach (var (projName, csprojPath, projIsLibrary, author, description, depUrls) in discoveredProjects)
                    {
                        var relPath = Path.GetRelativePath(localPath, csprojPath);
                        var key = Utils.HashFromString(repoUrl + "|" + relPath);

                        if (groupResult.NewEntries.ContainsKey(key))
                            continue;

                        var projOutputDir = GetPluginOutputPath(isSdkRepo ? projName : repoName);
                        var dllPath = ResolveProjectDll(projName, projOutputDir);

                        groupResult.NewEntries[key] = new PluginModel
                        {
                            PluginType = PluginType.Repo,
                            Name = projName,
                            RepoUrl = repoUrl,
                            ProjectFilePath = csprojPath,
                            IsLibrary = projIsLibrary,
                            AutoUpdate = representative.AutoUpdate,
                            Path = dllPath ?? string.Empty,
                            Author = author,
                            Description = description,
                            DependencyRepoUrls = depUrls != null ? new List<string>(depUrls) : new List<string>()
                        };
                    }
                }
                else
                {
                    // Update path on existing project entries
                    foreach (var (key, plugin) in projectEntries)
                    {
                        var projOutputDir = GetPluginOutputPath(isSdkRepo ? plugin.Name : repoName);
                        var dllPath = ResolveProjectDll(plugin.Name, projOutputDir);
                        if (dllPath != null)
                            plugin.Path = dllPath;
                        ApplyManifestToPlugin(plugin, localPath);
                    }
                }

                MergeAndNotify(result, groupResult, onGroupComplete);
            }

            ApplyPendingBootstrapSdkUpdates();
            return result;
        }

        private static void MergeAndNotify(CompileResult overall, CompileResult group, Action<CompileResult> callback)
        {
            foreach (var key in group.KeysToRemove) overall.KeysToRemove.Add(key);
            foreach (var kvp in group.NewEntries) overall.NewEntries[kvp.Key] = kvp.Value;
            if (!group.AllSucceeded) overall.AllSucceeded = false;
            callback?.Invoke(group);
        }

        /// <summary>
        /// Compiles a single repo plugin entry (used by the right-click context menu).
        /// For stub entries, builds the repo and returns expanded project models.
        /// </summary>
        public async Task<CompileResult> CompileOne(
            string pluginKey,
            PluginModel plugin,
            IEnumerable<(string packageId, string dllPath)> precompiledLibraries,
            bool pullFirst = true)
        {
            var singleEntry = new Dictionary<string, PluginModel> { { pluginKey, plugin } };
            var entries = singleEntry.Select(kvp => kvp).ToList();

            var result = new CompileResult();
            var localPath = GetLocalRepoPath(plugin.RepoUrl);

            Report(plugin.Name, $"Processing {plugin.RepoUrl}");

            if (pullFirst)
            {
                bool pulled = await Task.Run(() => CloneOrPull(plugin.RepoUrl, localPath));
                if (!pulled)
                {
                    Report(plugin.Name, "Failed to clone/pull.", isError: true);
                    result.AllSucceeded = false;
                    return result;
                }
            }
            else if (!Directory.Exists(Path.Combine(localPath, ".git")))
            {
                bool cloned = await Task.Run(() => CloneOrPull(plugin.RepoUrl, localPath));
                if (!cloned)
                {
                    Report(plugin.Name, "Failed to clone.", isError: true);
                    result.AllSucceeded = false;
                    return result;
                }
            }

            var combinedLibs = new List<(string packageId, string dllPath)>();
            AddBundledAoSharpSdkDllsWhereMissing(combinedLibs);
            MergePrecompiledLibraries(combinedLibs, precompiledLibraries);

            var urlsForThis = GetManifestDependencyUrlsForPlugin(plugin, localPath)
                .Where(u => !string.Equals(u, plugin.RepoUrl, StringComparison.OrdinalIgnoreCase));
            if (!await BuildAllManifestDependenciesAsync(plugin.Name, urlsForThis, plugin.RepoUrl, combinedLibs, pullFirst))
            {
                Report(plugin.Name, "Manifest dependency build failed.", isError: true);
                result.AllSucceeded = false;
                return result;
            }

            MergePrecompiledLibraries(combinedLibs, precompiledLibraries);

            var isSdkPlugin = string.Equals(plugin.RepoUrl, Config.AoSharpSdkRepoUrl, StringComparison.OrdinalIgnoreCase);
            if (!isSdkPlugin)
            {
                if (!await EnsureAoSharpSdkLibrariesAsync(plugin.Name, combinedLibs, pullFirst))
                {
                    result.AllSucceeded = false;
                    return result;
                }
            }

            await Task.Run(() => InjectReferenceOverrides(localPath, combinedLibs));

            await Task.Run(() => AlignCompiledRepoTargetFrameworks(localPath, plugin.Name));

            if (isSdkPlugin && plugin.IsLibrary)
            {
                if (!await BuildAoSharpSdkLibraryProjectsAsync(plugin.Name, localPath, combinedLibs, pullFirst: false))
                {
                    result.AllSucceeded = false;
                    return result;
                }

                if (plugin.IsStub)
                {
                    result.KeysToRemove.Add(pluginKey);
                    var discovered = await Task.Run(() => DiscoverProjects(localPath));
                    foreach (var (projName, csprojPath, projIsLibrary, author, description, depUrls) in discovered)
                    {
                        var relPath = Path.GetRelativePath(localPath, csprojPath);
                        var key = Utils.HashFromString(plugin.RepoUrl + "|" + relPath);
                        var dllPath = ResolveProjectDll(projName, GetPluginOutputPath(projName));
                        result.NewEntries[key] = new PluginModel
                        {
                            PluginType = PluginType.Repo,
                            Name = projName,
                            RepoUrl = plugin.RepoUrl,
                            ProjectFilePath = csprojPath,
                            IsLibrary = projIsLibrary,
                            AutoUpdate = plugin.AutoUpdate,
                            Path = dllPath ?? string.Empty,
                            Author = author,
                            Description = description,
                            DependencyRepoUrls = depUrls != null ? new List<string>(depUrls) : new List<string>()
                        };
                    }
                }
                else
                {
                    var dllPath = ResolveProjectDll(plugin.Name, GetPluginOutputPath(plugin.Name));
                    if (dllPath != null)
                        plugin.Path = dllPath;
                    ApplyManifestToPlugin(plugin, localPath);
                }

                return result;
            }

            var buildTarget = FindBuildTarget(localPath);
            if (buildTarget == null)
            {
                Report(plugin.Name, "No .csproj found.", isError: true);
                result.AllSucceeded = false;
                return result;
            }

            var primaryDll = await Task.Run(() => Build(buildTarget, plugin.Name));
            if (primaryDll == null)
            {
                result.AllSucceeded = false;
                return result;
            }

            if (plugin.IsStub)
            {
                result.KeysToRemove.Add(pluginKey);

                var outputDir = GetPluginOutputPath(plugin.Name);
                var discovered = await Task.Run(() => DiscoverProjects(localPath));

                foreach (var (projName, csprojPath, projIsLibrary, author, description, depUrls) in discovered)
                {
                    var relPath = Path.GetRelativePath(localPath, csprojPath);
                    var key = Utils.HashFromString(plugin.RepoUrl + "|" + relPath);
                    var dllPath = ResolveProjectDll(projName, outputDir);

                    result.NewEntries[key] = new PluginModel
                    {
                        PluginType = PluginType.Repo,
                        Name = projName,
                        RepoUrl = plugin.RepoUrl,
                        ProjectFilePath = csprojPath,
                        IsLibrary = projIsLibrary,
                        AutoUpdate = plugin.AutoUpdate,
                        Path = dllPath ?? string.Empty,
                        Author = author,
                        Description = description,
                        DependencyRepoUrls = depUrls != null ? new List<string>(depUrls) : new List<string>()
                    };
                }
            }
            else
            {
                var dllPath = ResolveProjectDll(plugin.Name, GetPluginOutputPath(plugin.Name));
                if (dllPath != null)
                    plugin.Path = dllPath;
                ApplyManifestToPlugin(plugin, localPath);
            }

            return result;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        public static string GetLocalRepoPath(string repoUrl)
        {
            if (string.IsNullOrEmpty(repoUrl))
                return null;

            return Path.Combine(Directories.ReposDirPath, Utils.HashFromString(repoUrl));
        }

        /// <summary>
        /// Returns a project file to build: prefers a .csproj in the repo root, otherwise the first .csproj under the tree (stable order).
        /// Does not use .sln so repos are not required to ship a solution file.
        /// </summary>
        public string FindBuildTarget(string localPath)
        {
            var root = Directory.GetFiles(localPath, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (root != null)
                return root;

            return Directory.GetFiles(localPath, "*.csproj", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        /// <summary>
        /// Finds the DLL for a given project name in the output directory.
        /// Looks for an exact name match first, then falls back to most-recent non-system DLL.
        /// </summary>
        private string ResolveProjectDll(string projectName, string outputDir)
        {
            if (!Directory.Exists(outputDir))
                return null;

            var exact = Path.Combine(outputDir, projectName + ".dll");
            if (File.Exists(exact))
                return exact;

            return Directory.GetFiles(outputDir, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).StartsWith("System.") &&
                            !Path.GetFileName(p).StartsWith("Microsoft.") &&
                            !p.Contains("\\ref\\"))
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        private bool RunGit(string workingDir, string args)
        {
            Directory.CreateDirectory(workingDir);
            return RunProcess("git", args, workingDir, new List<string>());
        }

        private bool RunProcessWithArgumentList(string executable, List<string> argumentList, string workingDir, List<string> outputLines)
        {
            try
            {
                var psi = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var a in argumentList)
                    psi.ArgumentList.Add(a);

                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        Log.Debug($"[{executable}] {e.Data}");
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        Log.Debug($"[{executable}:err] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to run {executable} {string.Join(" ", argumentList)}: {ex.Message}");
                outputLines.Add($"Exception: {ex.Message}");
                return false;
            }
        }

        private bool RunProcess(string executable, string args, string workingDir, List<string> outputLines)
        {
            try
            {
                var psi = new ProcessStartInfo(executable, args)
                {
                    WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        Log.Debug($"[{executable}] {e.Data}");
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        Log.Debug($"[{executable}:err] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to run {executable} {args}: {ex.Message}");
                outputLines.Add($"Exception: {ex.Message}");
                return false;
            }
        }
    }
}
