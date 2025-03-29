using RimSharp.Models;

namespace RimSharp.Services
{
    public class PathService : IPathService
    {
        private PathSettings _settings;

        public PathService(PathSettings settings)
        {
            _settings = settings;
        }

        public string GetGamePath()
        {
            return _settings.GamePath;
        }

        public string GetModsPath()
        {
            return _settings.ModsPath;
        }

        public string GetConfigPath()
        {
            return _settings.ConfigPath;
        }

        public string GetGameVersion()
        {
            return _settings.GameVersion;
        }
    }
}