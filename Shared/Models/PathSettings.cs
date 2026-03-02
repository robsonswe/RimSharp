using RimSharp.AppDir.AppFiles;

namespace RimSharp.Shared.Models
{
    public class PathSettings : ViewModelBase
    {
        private string _gamePath = string.Empty;
        private string _modsPath = string.Empty;
        private string _configPath = string.Empty;
        private string _gameVersion = string.Empty;

        public string GamePath
        {
            get => _gamePath;
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
    }
}
