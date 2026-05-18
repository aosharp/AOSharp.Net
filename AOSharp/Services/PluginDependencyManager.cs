using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Models;
using Serilog;

namespace AOSharp.Services
{
    /// <summary>
    /// Adds manifest-declared dependency plugins to config, blocks removal when depended on,
    /// and prunes auto-managed entries that are no longer referenced.
    /// </summary>
    public static class PluginDependencyManager
    {
        public static bool TryGetPluginKey(
            ManifestDependencyReference dep,
            string consumerRepoUrl,
            string consumerRepoLocalPath,
            RepoCompiler compiler,
            out string pluginKey,
            out string csprojPath,
            out string repoUrl)
        {
            pluginKey = null;
            csprojPath = null;
            repoUrl = dep.RepoUrl ?? consumerRepoUrl;

            if (string.IsNullOrEmpty(repoUrl))
                return false;

            var depLocal = string.IsNullOrEmpty(dep.RepoUrl)
                ? consumerRepoLocalPath
                : RepoCompiler.GetLocalRepoPath(dep.RepoUrl);

            if (string.IsNullOrEmpty(depLocal) || !Directory.Exists(depLocal))
                return false;

            csprojPath = ResolveCsprojPath(depLocal, dep, compiler);
            if (string.IsNullOrEmpty(csprojPath))
                return false;

            var relPath = Path.GetRelativePath(depLocal, csprojPath);
            pluginKey = Utils.HashFromString(repoUrl + "|" + relPath);
            return true;
        }

        /// <summary>
        /// Clones missing dependency repos and adds/updates <see cref="PluginModel.IsManifestDependency"/> entries.
        /// </summary>
        public static async Task EnsureDependenciesAsync(
            Config config,
            RepoCompiler compiler,
            PluginModel consumer,
            bool pullFirst = false)
        {
            if (config == null || compiler == null || consumer == null)
                return;

            if (consumer.PluginType != PluginType.Repo || string.IsNullOrEmpty(consumer.RepoUrl))
                return;

            var consumerLocal = RepoCompiler.GetLocalRepoPath(consumer.RepoUrl);
            if (string.IsNullOrEmpty(consumerLocal))
                return;

            if (!string.IsNullOrEmpty(consumer.ProjectFilePath) && File.Exists(consumer.ProjectFilePath))
                RepoCompiler.ApplyManifestToPlugin(consumer, consumerLocal);
            else if (consumer.IsStub)
                return;

            var projectDir = Path.GetDirectoryName(consumer.ProjectFilePath);
            var manifest = PluginManifest.LoadForProject(consumer.ProjectFilePath);
            consumer.DependencyRepoUrls = manifest.Dependencies != null
                ? new List<string>(manifest.Dependencies)
                : new List<string>();
            if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in manifest.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (!ManifestDependencyReference.TryParse(raw.Trim(), projectDir, consumerLocal, out var dep))
                {
                    Log.Warning("[PluginDependencyManager] Could not parse dependency '{Dep}' for {Plugin}",
                        raw, consumer.Name);
                    continue;
                }

                if (!seen.Add(dep.GetBuildKey()))
                    continue;

                await EnsureOneDependencyAsync(config, compiler, consumer.RepoUrl, consumerLocal, dep, pullFirst);
            }
        }

        private static async Task EnsureOneDependencyAsync(
            Config config,
            RepoCompiler compiler,
            string consumerRepoUrl,
            string consumerRepoLocalPath,
            ManifestDependencyReference dep,
            bool pullFirst)
        {
            var depLocal = string.IsNullOrEmpty(dep.RepoUrl)
                ? consumerRepoLocalPath
                : RepoCompiler.GetLocalRepoPath(dep.RepoUrl);

            if (!string.IsNullOrEmpty(dep.RepoUrl))
            {
                var needsClone = string.IsNullOrEmpty(depLocal) ||
                                 !Directory.Exists(depLocal) ||
                                 !Directory.Exists(Path.Combine(depLocal, ".git"));
                if (pullFirst || needsClone)
                {
                    depLocal = RepoCompiler.GetLocalRepoPath(dep.RepoUrl);
                    if (!await Task.Run(() => compiler.CloneOrPull(dep.RepoUrl, depLocal)))
                    {
                        Log.Warning("[PluginDependencyManager] Failed to clone dependency repo: {Url}", dep.RepoUrl);
                        return;
                    }
                }
            }
            else if (string.IsNullOrEmpty(depLocal) || !Directory.Exists(depLocal))
            {
                Log.Warning("[PluginDependencyManager] Consumer repo not available for dependency '{Dep}'", dep.RawEntry);
                return;
            }

            if (!TryGetPluginKey(dep, consumerRepoUrl, consumerRepoLocalPath, compiler,
                    out var key, out var csprojPath, out var repoUrl))
            {
                Log.Warning("[PluginDependencyManager] Could not resolve .csproj for dependency '{Dep}'", dep.RawEntry);
                return;
            }

            var manifest = PluginManifest.LoadForProject(csprojPath);
            var projName = Path.GetFileNameWithoutExtension(csprojPath);

            if (!config.Plugins.TryGetValue(key, out var existing))
            {
                config.Plugins.Add(key, new PluginModel
                {
                    PluginType = PluginType.Repo,
                    Name = projName,
                    RepoUrl = repoUrl,
                    ProjectFilePath = csprojPath,
                    IsLibrary = manifest.IsLibrary,
                    Section = manifest.ResolveSection(),
                    IsManifestDependency = true,
                    Path = ResolveExistingDllPath(projName) ?? string.Empty,
                    Author = manifest.Author,
                    Description = manifest.Description,
                    DependencyRepoUrls = manifest.Dependencies != null
                        ? new List<string>(manifest.Dependencies)
                        : new List<string>()
                });

                Log.Information("[PluginDependencyManager] Added manifest dependency {Name} ({Key})", projName, key);
            }
            else if (existing.IsManifestDependency)
            {
                existing.Name = projName;
                existing.RepoUrl = repoUrl;
                existing.ProjectFilePath = csprojPath;
                existing.IsLibrary = manifest.IsLibrary;
                existing.Section = manifest.ResolveSection();
                existing.Author = manifest.Author;
                existing.Description = manifest.Description;
                existing.DependencyRepoUrls = manifest.Dependencies != null
                    ? new List<string>(manifest.Dependencies)
                    : new List<string>();

                var dll = ResolveExistingDllPath(projName);
                if (!string.IsNullOrEmpty(dll))
                    existing.Path = dll;
            }

            var depPlugin = config.Plugins[key];
            await EnsureDependenciesAsync(config, compiler, depPlugin, pullFirst: false);
        }

        public static IReadOnlyList<PluginModel> GetDependents(
            Config config,
            string dependencyKey,
            RepoCompiler compiler)
        {
            var names = new List<PluginModel>();
            if (config?.Plugins == null || !config.Plugins.ContainsKey(dependencyKey))
                return names;

            foreach (var kvp in config.Plugins)
            {
                if (string.Equals(kvp.Key, dependencyKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (DependsOn(config, kvp.Value, dependencyKey, compiler))
                    names.Add(kvp.Value);
            }

            return names;
        }

        private static IEnumerable<string> GetDeclaredDependencies(PluginModel consumer)
        {
            if (consumer?.DependencyRepoUrls != null && consumer.DependencyRepoUrls.Count > 0)
                return consumer.DependencyRepoUrls;

            if (consumer?.PluginType == PluginType.Repo &&
                !string.IsNullOrEmpty(consumer.ProjectFilePath) &&
                File.Exists(consumer.ProjectFilePath))
            {
                var manifest = PluginManifest.LoadForProject(consumer.ProjectFilePath);
                if (manifest.Dependencies != null && manifest.Dependencies.Count > 0)
                    return manifest.Dependencies;
            }

            return Array.Empty<string>();
        }

        private static bool DependsOn(
            Config config,
            PluginModel consumer,
            string dependencyKey,
            RepoCompiler compiler)
        {
            if (string.IsNullOrEmpty(consumer?.RepoUrl))
                return false;

            var consumerLocal = RepoCompiler.GetLocalRepoPath(consumer.RepoUrl);
            if (string.IsNullOrEmpty(consumerLocal))
                return false;

            var projectDir = string.IsNullOrEmpty(consumer.ProjectFilePath)
                ? null
                : Path.GetDirectoryName(consumer.ProjectFilePath);

            foreach (var raw in GetDeclaredDependencies(consumer))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (!ManifestDependencyReference.TryParse(raw.Trim(), projectDir, consumerLocal, out var dep))
                    continue;

                if (TryGetPluginKey(dep, consumer.RepoUrl, consumerLocal, compiler, out var key, out _, out _) &&
                    string.Equals(key, dependencyKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool CanRemove(Config config, string key, RepoCompiler compiler, out string reason)
        {
            reason = null;
            if (config?.Plugins == null || !config.Plugins.TryGetValue(key, out var plugin))
                return false;

            if (plugin.IsDefault)
            {
                reason = "Built-in plugins cannot be removed.";
                return false;
            }

            if (!plugin.IsManifestDependency)
                return true;

            var dependents = GetDependents(config, key, compiler);
            if (dependents.Count > 0)
            {
                var names = string.Join(", ", dependents.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase));
                reason = $"Required by {names}.";
                return false;
            }

            return true;
        }

        /// <summary>Removes manifest-managed plugins that nothing references anymore.</summary>
        public static List<string> PruneOrphanDependencies(Config config, RepoCompiler compiler)
        {
            var removed = new List<string>();
            if (config?.Plugins == null)
                return removed;

            var candidates = config.Plugins
                .Where(kvp => kvp.Value.IsManifestDependency)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in candidates)
            {
                if (GetDependents(config, key, compiler).Count > 0)
                    continue;

                if (config.Plugins.TryGetValue(key, out var depPlugin) && config.Plugins.Remove(key))
                {
                    removed.Add(key);
                    RepoCompiler.DeletePluginArtifacts(depPlugin, config);
                    Log.Information("[PluginDependencyManager] Pruned orphan dependency {Key}", key);
                }
            }

            return removed;
        }

        public static void RemovePluginKeyFromLoadouts(Config config, string key)
        {
            if (config?.Loadouts == null || string.IsNullOrEmpty(key))
                return;

            foreach (var loadout in config.Loadouts)
            {
                if (loadout.PluginKeys == null)
                    continue;
                loadout.PluginKeys.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static string ResolveCsprojPath(
            string depLocal,
            ManifestDependencyReference dep,
            RepoCompiler compiler)
        {
            if (!string.IsNullOrEmpty(dep.CsprojRelativePath))
            {
                var normalized = dep.CsprojRelativePath.Replace('/', Path.DirectorySeparatorChar);
                var path = Path.GetFullPath(Path.Combine(depLocal, normalized));
                return File.Exists(path) ? path : null;
            }

            return compiler.FindBuildTarget(depLocal);
        }

        private static string ResolveExistingDllPath(string projectName)
        {
            if (string.IsNullOrEmpty(projectName))
                return null;

            var path = Path.Combine(RepoCompiler.GetPluginOutputPath(projectName), projectName + ".dll");
            return File.Exists(path) ? path : null;
        }
    }
}
