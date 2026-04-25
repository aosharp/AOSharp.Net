using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using AOSharp;
using AOSharp.Data;
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
        /// Returns all .csproj files found in the repo directory, along with their AOSharpLibrary flag.
        /// </summary>
        public List<(string name, string csprojPath, bool isLibrary)> DiscoverProjects(string localRepoPath)
        {
            return Directory
                .GetFiles(localRepoPath, "*.csproj", SearchOption.AllDirectories)
                .Select(p => (Path.GetFileNameWithoutExtension(p), p, ReadIsLibrary(p)))
                .OrderBy(p => p.Item1)
                .ToList();
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
        /// Builds a project or solution file, directing output to Plugins\{outputName}\.
        /// Returns the path to the primary output DLL, or null on failure.
        /// </summary>
        public string Build(string projectFile, string outputName)
        {
            Report(outputName, $"Building {Path.GetFileName(projectFile)}...");

            var outputPath = GetPluginOutputPath(outputName);
            Directory.CreateDirectory(outputPath);

            var outputLines = new List<string>();
            bool success = RunProcess("dotnet",
                $"build \"{projectFile}\" -c Release -p:Platform=x86 -p:OutputPath=\"{outputPath}\" --nologo",
                Path.GetDirectoryName(projectFile),
                outputLines);

            if (!success)
            {
                Report(outputName, "Build failed.", isError: true);
                foreach (var line in outputLines.Where(l => l.Contains("error")))
                    Report(outputName, line, isError: true);
                return null;
            }

            var dllPath = FindBuiltDll(outputName, outputLines);
            if (dllPath != null)
                Report(outputName, $"Built: {dllPath}");
            else
                Report(outputName, "Build succeeded but could not locate primary output DLL.", isError: true);

            return dllPath;
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

                // Inject reference overrides using everything compiled so far
                await Task.Run(() => InjectReferenceOverrides(localPath, compiledLibraries));

                // Find the top-level build target (.sln preferred, then .csproj)
                var buildTarget = FindBuildTarget(localPath);
                if (buildTarget == null)
                {
                    Report(repoName, "No .sln or .csproj found.", isError: true);
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

                    foreach (var (projName, csprojPath, projIsLibrary) in discoveredProjects)
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
                            Path = dllPath ?? string.Empty
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

            await Task.Run(() => InjectReferenceOverrides(localPath, precompiledLibraries));

            var buildTarget = FindBuildTarget(localPath);
            if (buildTarget == null)
            {
                Report(plugin.Name, "No .sln or .csproj found.", isError: true);
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

                foreach (var (projName, csprojPath, projIsLibrary) in discovered)
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
                        Path = dllPath ?? string.Empty
                    };
                }
            }
            else
            {
                var dllPath = ResolveProjectDll(plugin.Name, GetPluginOutputPath(plugin.Name));
                if (dllPath != null)
                    plugin.Path = dllPath;
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

        /// <summary>Returns the .sln if present, otherwise the first .csproj found.</summary>
        public string FindBuildTarget(string localPath)
        {
            var sln = Directory.GetFiles(localPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (sln != null)
                return sln;

            return Directory.GetFiles(localPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
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

        private string FindBuiltDll(string outputName, List<string> buildOutput)
        {
            // Parse build output for "-> path/to/something.dll"
            var arrowPattern = new Regex(@"->\s*(.+\.dll)", RegexOptions.IgnoreCase);
            foreach (var line in buildOutput)
            {
                var match = arrowPattern.Match(line);
                if (match.Success)
                {
                    var path = match.Groups[1].Value.Trim();
                    if (File.Exists(path))
                        return path;
                }
            }

            return ResolveProjectDll(outputName, GetPluginOutputPath(outputName));
        }

        private bool RunGit(string workingDir, string args)
        {
            Directory.CreateDirectory(workingDir);
            return RunProcess("git", args, workingDir, new List<string>());
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
