using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private static PluginManifest Normalize(PluginManifest m)
        {
            m.Author = m.Author?.Trim();
            m.Description = m.Description?.Trim();
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
                Dependencies = source.Dependencies != null
                    ? new List<string>(source.Dependencies)
                    : new List<string>()
            };
        }
    }
}
