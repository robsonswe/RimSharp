using RimSharp.Models;
using System.IO; // Needed for Path operations if doing real detection
using System;

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
               public string GetGameVersion(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return "N/A - No path specified";

            try
            {
                if (!Directory.Exists(gamePath))
                    return "N/A - Invalid path";

                string versionFilePath = Path.Combine(gamePath, "Version.txt");
                
                if (!File.Exists(versionFilePath))
                    return "N/A - Version.txt not found";

                string versionText = File.ReadAllText(versionFilePath).Trim();
                
                if (string.IsNullOrWhiteSpace(versionText))
                    return "N/A - Empty version file";

                // Additional parsing if needed (e.g., extract just version number)
                return versionText.Split('\n')[0].Trim(); // Get first line
            }
            catch (Exception ex)
            {
                return $"N/A - Error reading version: {ex.Message}";
            }
        }

        public string GetGameVersion()
        {
            return GetGameVersion(_settings.GamePath);
        }

    }
}
