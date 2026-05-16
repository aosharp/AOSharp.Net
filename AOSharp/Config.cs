using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace AOSharp
{
    public class Config
    {
        public const string AoSharpSdkKey = "aosharp-sdk-default";
        public const string AoSharpSdkRepoUrl = "https://github.com/aosharp/AOSharp.SDK";

        public ObservableDictionary<string, PluginModel> Plugins { get; set; }

        public ObservableCollection<Profile> Profiles { get; set; }

        protected string _path;

        public static Config Load(string path)
        {
            Config config;

            if (File.Exists(path))
            {
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
            }
            else
            {
                config = new Config()
                {
                    Plugins = new ObservableDictionary<string, PluginModel>(),
                    Profiles = new ObservableCollection<Profile>()
                };
            }

            config._path = path;
            config.EnsureDefaults();

            return config;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        /// <summary>
        /// Ensures built-in defaults (AOSharp.SDK) are present and marked correctly.
        /// Works whether the SDK exists as a single stub or has been expanded to per-project entries.
        /// </summary>
        public void EnsureDefaultsPublic() => EnsureDefaults();

        private void EnsureDefaults()
        {
            var sdkEntries = Plugins
                .Where(kvp => kvp.Value.RepoUrl == AoSharpSdkRepoUrl)
                .ToList();

            if (sdkEntries.Any())
            {
                foreach (var kvp in sdkEntries)
                    kvp.Value.IsDefault = true;
            }
            else
            {
                Plugins.Add(AoSharpSdkKey, new PluginModel
                {
                    Name = "AOSharp.SDK",
                    PluginType = PluginType.Repo,
                    RepoUrl = AoSharpSdkRepoUrl,
                    IsLibrary = true,
                    AutoUpdate = true,
                    IsDefault = true
                });
            }
        }

        /// <summary>
        /// Returns (packageId, dllPath) for every compiled <see cref="PluginModel.IsLibrary"/> plugin in the loader config.
        /// RepoCompiler merges these into reference injection so AOSharp SDK and other libraries always resolve from local builds (same idea as NuGet package substitution).
        /// </summary>
        public IEnumerable<(string packageId, string dllPath)> GetCompiledLibraryPaths()
        {
            foreach (var kvp in Plugins)
            {
                var plugin = kvp.Value;
                if (plugin.IsLibrary && plugin.IsCompiled)
                {
                    yield return (System.IO.Path.GetFileNameWithoutExtension(plugin.Path), plugin.Path);
                }
            }
        }
    }
}
