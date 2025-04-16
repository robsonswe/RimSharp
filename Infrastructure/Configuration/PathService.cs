#nullable enable
using System;
using System.IO;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Configuration
{
    public class PathService : IPathService
    {
        private readonly IConfigService _configService;
        private string? _cachedGamePath;
        private string? _cachedConfigPath;
        private string? _cachedModsPath; // Cache for derived path
        private string? _cachedGameVersion;
        private string? _cachedMajorGameVersion;
        private bool _isGamePathResolved = false; // Flag to track if game path has been resolved

        public PathService(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public string GetGamePath()
        {
            // Cache the resolved game path to avoid repeated config reads/checks
            if (!_isGamePathResolved)
            {
                var path = _configService.GetConfigValue("game_folder");
                _cachedGamePath = Directory.Exists(path) ? path : string.Empty;
                _isGamePathResolved = true; // Mark as resolved for this instance lifecycle or until invalidated

                // When GamePath changes, ModsPath *must* be invalidated
                InvalidateModsPathCache();
            }
            return _cachedGamePath ?? string.Empty; // Return empty if cache somehow null
        }

        public string GetModsPath()
        {
            // Mods path depends on Game Path, so resolve Game Path first if needed
            string gamePath = GetGamePath();

            // Use cached mods path if available
            if (_cachedModsPath != null)
            {
                return _cachedModsPath;
            }

            // Derive mods path only if game path is valid
            if (!string.IsNullOrEmpty(gamePath))
            {
                string derivedModsPath = Path.Combine(gamePath, "Mods");
                // Optionally check if the derived path exists, or just return it
                // For consistency with other Get methods, let's check existence
                _cachedModsPath = Directory.Exists(derivedModsPath) ? derivedModsPath : string.Empty;
            }
            else
            {
                _cachedModsPath = string.Empty; // No game path, no mods path
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
            // Game version depends on Game Path
            string gamePath = GetGamePath();
            if (_cachedGameVersion is null)
            {
                _cachedGameVersion = GetGameVersion(gamePath); // Use the overload
            }
            return _cachedGameVersion;
        }

        // Overload remains the same
        public string GetGameVersion(string gamePath)
        {
             if (string.IsNullOrEmpty(gamePath))
                 return "N/A - No path specified";

             // Cache the version based *on this specific game path* if called directly
             // However, the main _cachedGameVersion depends on the GetGamePath() result
             // For simplicity, let's just calculate it directly here without caching tied to the parameter path
             // The main _cachedGameVersion will be updated via GetGameVersion() (no params) when needed

             try
             {
                 var versionFilePath = Path.Combine(gamePath, "Version.txt");

                 if (!File.Exists(versionFilePath))
                     return "N/A - Version.txt not found";

                 using var reader = new StreamReader(versionFilePath);
                 var firstLine = reader.ReadLine()?.Trim() ?? string.Empty;

                 var result = string.IsNullOrWhiteSpace(firstLine)
                     ? "N/A - Empty version file"
                     : firstLine;

                 // Update the primary cache if the path matches the service's current game path
                 if (gamePath == _cachedGamePath)
                 {
                     _cachedGameVersion = result;
                 }
                 return result;

             }
             catch (Exception ex)
             {
                var errorMsg = $"N/A - Error reading version: {ex.Message.Truncate(50)}";
                 // Update the primary cache if the path matches the service's current game path
                 if (gamePath == _cachedGamePath)
                 {
                     _cachedGameVersion = errorMsg;
                 }
                 return errorMsg;
             }
        }

        // Major version logic depends on GetGameVersion, no changes needed here
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
                return fullVersion; // Or return "N/A"? Let's return original for now.

            // Handle versions like "1.4.3529 rev704" or "1.5"
            var secondPart = versionParts[1].Split(new[] { ' ', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return $"{versionParts[0]}.{secondPart}";
        }


        // Method to invalidate caches when configs change or refresh is needed
        public void InvalidateCache()
        {
            // Reset resolution flag
            _isGamePathResolved = false;

            // Clear caches
            _cachedGamePath = null;
            _cachedConfigPath = null;
            _cachedModsPath = null; // Must clear mods path when game path might change
            _cachedGameVersion = null;
            _cachedMajorGameVersion = null;
        }

        // Helper to invalidate just the mods path cache if needed (e.g., when game path changes)
        private void InvalidateModsPathCache()
        {
            _cachedModsPath = null;
        }

        public void RefreshPaths()
        {
            // Invalidate cache and reload paths
            InvalidateCache();
            // Force refresh by accessing properties which will re-read config/derive paths
            var _ = GetGamePath();      // Resolves GamePath, potentially invalidates/resolves ModsPath cache
            var __ = GetModsPath();     // Resolves ModsPath based on potentially new GamePath
            var ___ = GetConfigPath();  // Resolves ConfigPath
            var ____ = GetGameVersion(); // Resolves Version based on potentially new GamePath
            var _____ = GetMajorGameVersion(); // Resolves MajorVersion
        }
    }

    // Helper extension method (optional, place appropriately)
    internal static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
