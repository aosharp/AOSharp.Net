using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading.Tasks;

namespace AOSharp.Models
{
    public enum AddPluginTab
    {
        Dll,
        Repo
    }

    public class AddAssemblyModel : INotifyPropertyChanged
    {
        private string _dllPath;
        private AddPluginTab _activeTab = AddPluginTab.Dll;

        public string DllPath
        {
            get { return _dllPath; }
            set
            {
                _dllPath = value;
                OnPropertyChanged(nameof(DllPath));
            }
        }

        public AddPluginTab ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                OnPropertyChanged(nameof(ActiveTab));
                OnPropertyChanged(nameof(IsDllTabActive));
                OnPropertyChanged(nameof(IsRepoTabActive));
            }
        }

        public bool IsDllTabActive => _activeTab == AddPluginTab.Dll;
        public bool IsRepoTabActive => _activeTab == AddPluginTab.Repo;

        public AddRepoModel RepoModel { get; set; } = new AddRepoModel();

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
