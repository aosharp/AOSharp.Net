using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using Newtonsoft.Json;
using System.Windows.Input;

namespace AOSharp
{
    public enum PluginType
    {
        Dll,
        Repo
    }

    public class PluginModel : INotifyPropertyChanged
    {
        public PluginType PluginType { get; set; } = PluginType.Dll;

        public string Name { get; set; }

        public string Version { get; set; }

        /// <summary>
        /// For Dll plugins: path to the DLL. For Repo plugins: path to the compiled DLL output (empty until compiled).
        /// </summary>
        public string Path { get; set; }

        public string RepoUrl { get; set; }

        /// <summary>
        /// Absolute path to the .csproj this entry represents within the cloned repo.
        /// Null for DLL plugins and for uncompiled repo stubs.
        /// </summary>
        public string ProjectFilePath { get; set; }

        /// <summary>
        /// True when this is a repo entry that has not yet been expanded to per-project entries.
        /// </summary>
        [JsonIgnore]
        public bool IsStub => PluginType == PluginType.Repo && string.IsNullOrEmpty(ProjectFilePath);

        public bool AutoUpdate { get; set; }

        /// <summary>
        /// When true the user has confirmed they trust this repo and update confirmations are skipped.
        /// </summary>
        public bool TrustedRepo { get; set; }

        /// <summary>
        /// Runtime flag set by the background update checker. Not persisted.
        /// </summary>
        [JsonIgnore]
        public bool HasUpdate { get; set; }

        /// <summary>
        /// Short commit hash of the currently checked-out local clone. Runtime only.
        /// </summary>
        [JsonIgnore]
        public string LocalCommit { get; set; }

        /// <summary>
        /// Short commit hash of the remote HEAD (populated after git fetch). Runtime only.
        /// </summary>
        [JsonIgnore]
        public string RemoteCommit { get; set; }

        /// <summary>
        /// Libraries are compiled and referenced but never injected into the game process.
        /// </summary>
        public bool IsLibrary { get; set; }

        [JsonIgnore]
        public bool IsDefault { get; set; }

        [JsonIgnore]
        public bool IsCompiled => PluginType == PluginType.Dll
            ? (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            : (!string.IsNullOrEmpty(Path) && File.Exists(Path));

        [JsonIgnore]
        public bool _isEnabled;

        [JsonIgnore]
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value;
                OnPropertyChanged("IsEnabled");
            }
        }

        [JsonIgnore]
        public ICommand RemoveCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public PluginModel()
        {
            this.RemoveCommand = new SimpleCommand() { ExecuteDelegate = Remove };
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void Remove(object obj)
        {
            if (IsDefault)
                return;

            var args = (Tuple<ObservableDictionary<string, PluginModel>, string>)obj;
            args.Item1.Remove(args.Item2, this);
        }
    }
}
