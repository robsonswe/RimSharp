using RimSharp.Models;
using System.IO; // Needed for Path operations if doing real detection

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

        // Original method: gets version based on the path currently in _settings
        public string GetGameVersion()
        {
             // Could call the specific path version, or return the stored value
             // Option 1: Return stored value
             // return _settings.GameVersion;
             // Option 2: Detect based on current GamePath (if GamePath is set)
             if (!string.IsNullOrEmpty(_settings.GamePath)) {
                 return GetGameVersion(_settings.GamePath);
             }
             return "N/A"; // Or return stored value if detection isn't desired here
        }

        // New overload: gets version based on the provided path
        public string GetGameVersion(string gamePath)
        {
            // TODO: Implement actual game version detection logic here!
            // This might involve checking specific file versions (e.g., RimWorldWin64.exe)
            // or reading a version file within the game directory.
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                return "N/A - Invalid Path";
            }

            // Placeholder implementation:
            // In a real app, you'd check file versions, etc.
            // For example:
            // string exePath = Path.Combine(gamePath, "RimWorldWin64.exe");
            // if (File.Exists(exePath)) {
            //     var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
            //     return versionInfo.FileVersion ?? "N/A - No Version";
            // }

            return "1.x.xxxx (Detected)"; // Return a dummy detected version for now
        }
    }
}
