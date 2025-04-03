#nullable enable
using System;
using System.IO;

namespace RimSharp.Services
{
    public class PathService : IPathService
    {
        private readonly IConfigService _configService;
        private string? _cachedGamePath;
        private string? _cachedConfigPath;
        private string? _cachedModsPath;
        private string? _cachedGameVersion;
        private string? _cachedMajorGameVersion;

        public PathService(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public string GetGamePath()
        {
            if (_cachedGamePath is null)
            {
                var path = _configService.GetConfigValue("game_folder");
                _cachedGamePath = Directory.Exists(path) ? path : string.Empty;
            }
            return _cachedGamePath;
        }

        public string GetModsPath()
        {
            if (_cachedModsPath is null)
            {
                var path = _configService.GetConfigValue("mods_folder");
                _cachedModsPath = Directory.Exists(path) ? path : string.Empty;
            }
            return _cachedModsPath;
        }

        public string GetConfigPath()
        {
            if (_cachedConfigPath is null)
            {
                var path = _configService.GetConfigValue("config_folder");
                _cachedConfigPath = Directory.Exists(path) ? path : string.Empty;
            }
            return _cachedConfigPath;
        }

        public string GetGameVersion()
        {
            if (_cachedGameVersion is null)
            {
                _cachedGameVersion = GetGameVersion(GetGamePath());
            }
            return _cachedGameVersion;
        }

        public string GetGameVersion(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return "N/A - No path specified";

            try
            {
                var versionFilePath = Path.Combine(gamePath, "Version.txt");

                if (!File.Exists(versionFilePath))
                    return "N/A - Version.txt not found";

                using var reader = new StreamReader(versionFilePath);
                var firstLine = reader.ReadLine()?.Trim() ?? string.Empty;

                return string.IsNullOrWhiteSpace(firstLine)
                    ? "N/A - Empty version file"
                    : firstLine;
            }
            catch (Exception ex)
            {
                return $"N/A - Error reading version: {ex.Message}";
            }
        }

        public string GetMajorGameVersion()
        {
            if (_cachedMajorGameVersion is null)
            {
                _cachedMajorGameVersion = ExtractMajorVersion(GetGameVersion());
            }
            return _cachedMajorGameVersion;
        }

        public string GetMajorGameVersion(string gamePath) =>
            ExtractMajorVersion(GetGameVersion(gamePath));

        private static string ExtractMajorVersion(string fullVersion)
        {
            if (string.IsNullOrEmpty(fullVersion) || fullVersion.StartsWith("N/A"))
                return fullVersion;

            var versionParts = fullVersion.Split('.');
            if (versionParts.Length < 2)
                return fullVersion;

            var secondPart = versionParts[1].Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return $"{versionParts[0]}.{secondPart}";
        }

        // Method to invalidate caches when configs change
        public void InvalidateCache()
        {
            _cachedGamePath = null;
            _cachedConfigPath = null;
            _cachedModsPath = null;
            _cachedGameVersion = null;
            _cachedMajorGameVersion = null;
        }

        public void RefreshPaths()
        {
            // Invalidate cache and reload paths
            InvalidateCache();
            // Force refresh by accessing properties
            var _ = GetGamePath();
            var __ = GetModsPath();
            var ___ = GetConfigPath();
            var ____ = GetGameVersion();
        }

    }
}