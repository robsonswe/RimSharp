using RimSharp.Models;
using System.IO; // Needed for Path operations if doing real detection
using System;

namespace RimSharp.Services
{
    public class PathService : IPathService
    {
        private readonly IConfigService _configService;
        private PathSettings _settings;

        public PathService(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            
            // Initialize PathSettings from ConfigService
            _settings = new PathSettings
            {
                GamePath = _configService.GetConfigValue("game_folder"),
                ConfigPath = _configService.GetConfigValue("config_folder"),
                ModsPath = _configService.GetConfigValue("mods_folder")
            };
        }

        // Method to refresh paths from config service
        public void RefreshPaths()
        {
            _settings.GamePath = _configService.GetConfigValue("game_folder");
            _settings.ConfigPath = _configService.GetConfigValue("config_folder");
            _settings.ModsPath = _configService.GetConfigValue("mods_folder");
        }

        public string GetGamePath()
        {
            RefreshPaths(); // Always get the latest values
            return Directory.Exists(_settings.GamePath) ? _settings.GamePath : null;
        }
        
        public string GetModsPath()
        {
            RefreshPaths(); // Always get the latest values
            return Directory.Exists(_settings.ModsPath) ? _settings.ModsPath : null;
        }
        
        public string GetConfigPath()
        {
            RefreshPaths(); // Always get the latest values
            return Directory.Exists(_settings.ConfigPath) ? _settings.ConfigPath : null;
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
            RefreshPaths(); // Always get the latest values
            return GetGameVersion(_settings.GamePath);
        }

        public string GetMajorGameVersion(string gamePath)
        {
            string fullVersion = GetGameVersion(gamePath);
            return ExtractMajorVersion(fullVersion);
        }

        public string GetMajorGameVersion()
        {
            RefreshPaths(); // Always get the latest values
            return GetMajorGameVersion(_settings.GamePath);
        }

        private string ExtractMajorVersion(string fullVersion)
        {
            if (string.IsNullOrEmpty(fullVersion))
                return string.Empty;

            // Handle cases where version starts with "N/A"
            if (fullVersion.StartsWith("N/A"))
                return fullVersion;

            try
            {
                // Extract just the first two numbers (e.g., "1.5" from "1.5.4409 rev1118")
                var versionParts = fullVersion.Split('.');
                if (versionParts.Length >= 2)
                {
                    return $"{versionParts[0]}.{versionParts[1].Split(' ')[0].Split('-')[0]}";
                }
                return fullVersion; // Fallback if we can't parse it
            }
            catch
            {
                return fullVersion; // Fallback if parsing fails
            }
        }
    }
}