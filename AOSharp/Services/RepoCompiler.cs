using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
        /// <summary>Fallback when AOSharp.csproj cannot be found (keep in sync with Loader AOSharp.csproj).</summary>
        private const string LoaderTargetFrameworkFallback = "net10.0-windows";

        private static string _cachedLoaderTargetFramework;

        public event EventHandler<CompileProgressEventArgs> Progress;

        private void Report(string pluginName, string message, bool isError = false)
        {
            Log.Information($"[RepoCompiler] {pluginName}: {message}");
            Progress?.Invoke(this, new CompileProgressEventArgs
            {
                PluginName = pluginName,
                Message = message,
                IsError = isError
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
        /// Writes AOSharpLoader.props into the repo root, substituting known NuGet packages
        /// with locally compiled DLL references. Ensures Directory.Build.props imports it.
        /// </summary>
        public void InjectReferenceOverrides(string localPath, IEnumerable<(string packageId, string dllPath)> localLibraries)
        {
            var propsPath = Path.Combine(localPath, "AOSharpLoader.props");
            var sb = new StringBuilder();
            sb.AppendLine("<Project>");
            sb.AppendLine("  <ItemGroup>");

            foreach (var (packageId, dllPath) in localLibraries)
            {
                if (!File.Exists(dllPath))
                    continue;

                sb.AppendLine($"    <!-- Override {packageId} with local build -->");
                sb.AppendLine($"    <PackageReference Update=\"{packageId}\" ExcludeAssets=\"all\" PrivateAssets=\"all\" />");
                sb.AppendLine($"    <Reference Include=\"{packageId}\">");
                sb.AppendLine($"      <HintPath>{dllPath}</HintPath>");
                sb.AppendLine($"      <Private>false</Private>");
                sb.AppendLine($"    </Reference>");
            }

            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine("</Project>");
            File.WriteAllText(propsPath, sb.ToString());

            var dirBuildProps = Path.Combine(localPath, "Directory.Build.props");
            string importLine = $"  <Import Project=\"AOSharpLoader.props\" Condition=\"Exists('AOSharpLoader.props')\" />";

            if (File.Exists(dirBuildProps))
            {
                var content = File.ReadAllText(dirBuildProps);
                if (!content.Contains("AOSharpLoader.props"))
                {
                    content = content.Replace("</Project>", importLine + Environment.NewLine + "</Project>");
                    File.WriteAllText(dirBuildProps, content);
                }
            }
            else
            {
                File.WriteAllText(dirBuildProps, $"<Project>{Environment.NewLine}{importLine}{Environment.NewLine}</Project>");
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
                "--nologo"
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

        /// <summary>
        /// Builds a project to its default output folder, then copies DLLs into Plugins\{outputName}\.
        /// Returns the path to the primary output DLL under Plugins, or null on failure.
        /// </summary>
        public string Build(string projectFile, string outputName)
        {
            Report(outputName, $"Building {Path.GetFileName(projectFile)}...");

            var pluginOutputDir = GetPluginOutputPath(outputName);
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

            try
            {
                CopyTopLevelDllsFromBuildOutput(Path.GetDirectoryName(builtDll), pluginOutputDir);
            }
            catch (Exception ex)
            {
                Report(outputName, $"Build output copy failed: {ex.Message}", isError: true);
                return null;
            }

            var deployed = Path.Combine(pluginOutputDir, Path.GetFileName(builtDll));
            Report(outputName, File.Exists(deployed) ? $"Built: {deployed}" : $"Built: {builtDll}");
            return File.Exists(deployed) ? deployed : builtDll;
        }

        /// <summary>Copies every DLL from the project's build output directory into the plugin folder (top-level only, no ref/).</summary>
        private static void CopyTopLevelDllsFromBuildOutput(string buildOutDir, string pluginOutputDir)
        {
            if (string.IsNullOrEmpty(buildOutDir) || !Directory.Exists(buildOutDir))
                return;

            Directory.CreateDirectory(pluginOutputDir);
            foreach (var dll in Directory.GetFiles(buildOutDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var dest = Path.Combine(pluginOutputDir, Path.GetFileName(dll));
                File.Copy(dll, dest, overwrite: true);
            }
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
                    .FirstOrDefault(p => !IsUnderRefDirectory(p));
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUnderRefDirectory(string filePath)
        {
            var sep = Path.DirectorySeparatorChar;
            return filePath.Contains($"{sep}ref{sep}", StringComparison.OrdinalIgnoreCase);
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
                    p.StartsWith(projDir, StringComparison.OrdinalIgnoreCase) && !IsUnderRefDirectory(p));
                if (underProj != null)
                    return underProj;
            }

            return candidates.LastOrDefault(p => !IsUnderRefDirectory(p))
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

                var outputDir = GetPluginOutputPath(outFolder);
                if (Directory.Exists(outputDir))
                {
                    foreach (var dll in Directory.GetFiles(outputDir, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        var id = Path.GetFileNameWithoutExtension(dll);
                        if (!compiledLibraries.Any(l =>
                                string.Equals(l.packageId, id, StringComparison.OrdinalIgnoreCase)))
                            compiledLibraries.Add((id, dll));
                    }
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
        /// Returns the accumulated CompileResult for all groups.
        /// </summary>
        public async Task<CompileResult> CompileAll(
            IEnumerable<KeyValuePair<string, PluginModel>> plugins,
            bool pullFirst = true,
            Action<CompileResult> onGroupComplete = null)
        {
            var result = new CompileResult();
            var compiledLibraries = new List<(string packageId, string dllPath)>();

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

                // Inject reference overrides using everything compiled so far
                await Task.Run(() => InjectReferenceOverrides(localPath, compiledLibraries));

                await Task.Run(() => AlignCompiledRepoTargetFrameworks(localPath, repoName));

                var buildTarget = FindBuildTarget(localPath);
                if (buildTarget == null)
                {
                    Report(repoName, "No .csproj found.", isError: true);
                    groupResult.AllSucceeded = false;
                    MergeAndNotify(result, groupResult, onGroupComplete);
                    continue;
                }

                // Build the whole repo into Plugins\{repoName}\
                var primaryDll = await Task.Run(() => Build(buildTarget, repoName));
                if (primaryDll == null)
                {
                    groupResult.AllSucceeded = false;
                    MergeAndNotify(result, groupResult, onGroupComplete);
                    continue;
                }

                // Register all DLLs in the output folder as available local references
                var outputDir = GetPluginOutputPath(repoName);
                foreach (var dll in Directory.GetFiles(outputDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var id = Path.GetFileNameWithoutExtension(dll);
                    if (!compiledLibraries.Any(l => l.packageId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                        compiledLibraries.Add((id, dll));
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

                        var dllPath = ResolveProjectDll(projName, outputDir);

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
                        var dllPath = ResolveProjectDll(plugin.Name, outputDir);
                        if (dllPath != null)
                            plugin.Path = dllPath;
                        ApplyManifestToPlugin(plugin, localPath);
                    }
                }

                MergeAndNotify(result, groupResult, onGroupComplete);
            }

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

            var combinedLibs = precompiledLibraries != null
                ? new List<(string packageId, string dllPath)>(precompiledLibraries)
                : new List<(string packageId, string dllPath)>();

            var urlsForThis = GetManifestDependencyUrlsForPlugin(plugin, localPath)
                .Where(u => !string.Equals(u, plugin.RepoUrl, StringComparison.OrdinalIgnoreCase));
            if (!await BuildAllManifestDependenciesAsync(plugin.Name, urlsForThis, plugin.RepoUrl, combinedLibs, pullFirst))
            {
                Report(plugin.Name, "Manifest dependency build failed.", isError: true);
                result.AllSucceeded = false;
                return result;
            }

            await Task.Run(() => InjectReferenceOverrides(localPath, combinedLibs));

            await Task.Run(() => AlignCompiledRepoTargetFrameworks(localPath, plugin.Name));

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
