using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp;
using Newtonsoft.Json;

namespace AOSharp.Models
{
    /// <summary>
    /// Optional <c>Manifest.json</c> in the same folder as the project <c>.csproj</c> (one manifest per project file).
    /// </summary>
    public class PluginManifest
    {
        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("library")]
        public bool? Library { get; set; }

        /// <summary>One of <see cref="PluginSections"/> (except libraries, which are always Library).</summary>
        [JsonProperty("section")]
        public string Section { get; set; }

        /// <summary>Git repo URLs, optional <c>/path/Project.csproj</c> suffix, or a <c>.csproj</c> path relative to this project.</summary>
        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; }

        public static PluginManifest Empty { get; } = new PluginManifest
        {
            Dependencies = new List<string>()
        };

        /// <summary>
        /// Loads <c>Manifest.json</c> from the same directory as <paramref name="csprojPath"/> only.
        /// </summary>
        public static PluginManifest LoadForProject(string csprojPath)
        {
            if (string.IsNullOrEmpty(csprojPath))
                return CloneDefaults(Empty);

            var projectDir = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrEmpty(projectDir))
                return CloneDefaults(Empty);

            var manifestPath = Path.Combine(projectDir, "Manifest.json");
            if (!File.Exists(manifestPath))
                return CloneDefaults(Empty);

            try
            {
                var json = File.ReadAllText(manifestPath);
                var m = JsonConvert.DeserializeObject<PluginManifest>(json);
                if (m != null)
                    return Normalize(m);
            }
            catch
            {
                // ignore invalid manifest
            }

            return CloneDefaults(Empty);
        }

        public bool IsLibrary => Library == true;

        public string ResolveSection()
        {
            if (IsLibrary)
                return PluginSections.Library;
            return PluginSections.Normalize(Section);
        }

        /// <summary>Reads manifest metadata for a repo project, or falls back to stored plugin fields.</summary>
        public static bool GetEffectiveIsLibrary(PluginModel plugin)
        {
            if (TryLoadForPlugin(plugin, out var manifest))
                return manifest.IsLibrary;
            return plugin.IsLibrary;
        }

        public static string GetEffectiveSection(PluginModel plugin)
        {
            if (GetEffectiveIsLibrary(plugin))
                return PluginSections.Library;
            if (TryLoadForPlugin(plugin, out var manifest))
                return manifest.ResolveSection();
            return PluginSections.Normalize(plugin.Section);
        }

        private static bool TryLoadForPlugin(PluginModel plugin, out PluginManifest manifest)
        {
            manifest = null;
            if (plugin?.PluginType != PluginType.Repo || string.IsNullOrEmpty(plugin.ProjectFilePath) ||
                !File.Exists(plugin.ProjectFilePath))
                return false;

            manifest = LoadForProject(plugin.ProjectFilePath);
            return true;
        }

        private static PluginManifest Normalize(PluginManifest m)
        {
            m.Author = m.Author?.Trim();
            m.Description = m.Description?.Trim();
            m.Section = m.Section?.Trim();
            m.Dependencies = m.Dependencies?
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            return m;
        }

        private static PluginManifest CloneDefaults(PluginManifest source)
        {
            return new PluginManifest
            {
                Author = source.Author,
                Description = source.Description,
                Library = source.Library,
                Section = source.Section,
                Dependencies = source.Dependencies != null
                    ? new List<string>(source.Dependencies)
                    : new List<string>()
            };
        }
    }
}
