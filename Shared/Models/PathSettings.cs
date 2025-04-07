using RimSharp.MyApp.AppFiles;

namespace RimSharp.Shared.Models
{
    // Inherit from ViewModelBase to get INotifyPropertyChanged implementation easily
    public class PathSettings : ViewModelBase
    {
        private string _gamePath;
        private string _modsPath;
        private string _configPath;
        private string _gameVersion;

        public string GamePath
        {
            get => _gamePath;
            // Use SetProperty from ViewModelBase to automatically raise PropertyChanged
            set => SetProperty(ref _gamePath, value);
        }

        public string ModsPath
        {
            get => _modsPath;
            set => SetProperty(ref _modsPath, value);
        }

        public string ConfigPath
        {
            get => _configPath;
            set => SetProperty(ref _configPath, value);
        }

        public string GameVersion
        {
            get => _gameVersion;
            set => SetProperty(ref _gameVersion, value);
        }

        // If NOT inheriting from ViewModelBase, you would need this boilerplate:
        // public event PropertyChangedEventHandler PropertyChanged;
        // protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        // {
        //     PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        // }
        // protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        // {
        //     if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        //     field = value;
        //     OnPropertyChanged(propertyName);
        //     return true;
        // }
    }
}
