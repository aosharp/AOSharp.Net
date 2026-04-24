using System.ComponentModel;

namespace AOSharp.Models
{
    public class AddRepoModel : INotifyPropertyChanged
    {
        private string _repoUrl;
        private bool _isLibrary;
        private bool _autoUpdate = true;

        public string RepoUrl
        {
            get => _repoUrl;
            set { _repoUrl = value; OnPropertyChanged(nameof(RepoUrl)); }
        }

        public bool IsLibrary
        {
            get => _isLibrary;
            set { _isLibrary = value; OnPropertyChanged(nameof(IsLibrary)); }
        }

        public bool AutoUpdate
        {
            get => _autoUpdate;
            set { _autoUpdate = value; OnPropertyChanged(nameof(AutoUpdate)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
