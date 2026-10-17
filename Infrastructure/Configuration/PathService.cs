#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Configuration
{
    public class PathService : IPathService
    {
        private readonly IConfigService _configService;
        private readonly Func<string, string?>? _fileVersionProbe; // Test seam for file-version probing
        private string? _cachedGamePath;
        private string? _cachedConfigPath;
        private string? _cachedModsPath; // Cache for derived path
        private string? _cachedGameVersion;
        private string? _cachedMajorGameVersion;
        private bool _isGamePathResolved = false; // Flag to track if game path has been resolved

        public PathService(IConfigService configService)
            : this(configService, null)
        {
        }

        // Secondary constructor exists so unit tests can stub out file-version probing.
        public PathService(IConfigService configService, Func<string, string?>? fileVersionProbe)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _fileVersionProbe = fileVersionProbe;
        }

        public string GetGamePath()
        {

            if (!_isGamePathResolved)
            {
                var path = _configService.GetConfigValue("game_folder");
                _cachedGamePath = Directory.Exists(path) ? path : string.Empty;
                _isGamePathResolved = true; // Mark as resolved for this instance lifecycle or until invalidated

                // When GamePath changes, ModsPath *must* be invalidated
                InvalidateModsPathCache();
            }
            return _cachedGamePath ?? string.Empty;
        }

        public string GetModsPath()
        {

            string gamePath = GetGamePath();

            if (_cachedModsPath != null)
            {
                return _cachedModsPath;
            }
            if (!string.IsNullOrEmpty(gamePath))
            {
                string derivedModsPath = Path.Combine(gamePath, "Mods");

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

        // Ordered fallback chain for game-version detection, covering both engine generations:
        //   - Waylith (.NET-native, 1.7+): RimWorld.exe / RimWorld.dll carry the game version
        //     in their file-version metadata (e.g. "1.7.4979.109").
        //   - Unity (1.6 and earlier): <player-data>/Managed/Assembly-CSharp.dll carries it
        //     (e.g. "1.6.9438.37837"). The player-data folder name differs per platform
        //     (RimWorldWin64_Data on Windows, RimWorld_Data / RimWorldLinux_Data elsewhere).
        // RimWorldWin64.exe is deliberately NOT a candidate: its metadata holds the Unity
        // engine version (e.g. "2022.3.35f1"), not the game version. The sanity guard in
        // NormalizeGameVersion would reject it anyway.
        private static readonly string[] GameVersionFileCandidates =
        {
            "RimWorld.exe",
            "RimWorld.dll",
            Path.Combine("RimWorldWin64_Data", "Managed", "Assembly-CSharp.dll"),
            Path.Combine("RimWorld_Data", "Managed", "Assembly-CSharp.dll"),
            Path.Combine("RimWorldLinux_Data", "Managed", "Assembly-CSharp.dll"),
        };

        // Overload remains the same
        public string GetGameVersion(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return "N/A - No path specified";

            try
            {
                // Primary source: Version.txt shipped in the game root.
                var versionFilePath = Path.Combine(gamePath, "Version.txt");
                if (File.Exists(versionFilePath))
                {
                    using var reader = new StreamReader(versionFilePath);
                    var firstLine = reader.ReadLine()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(firstLine))
                        return CacheVersion(gamePath, firstLine);
                }

                // Fallback: version metadata embedded in the game binaries, for builds
                // that ship without Version.txt.
                var fallbackVersion = GetVersionFromFileMetadata(gamePath);
                if (fallbackVersion != null)
                    return CacheVersion(gamePath, fallbackVersion);

                return CacheVersion(gamePath, "N/A - No version source found");
            }
            catch (Exception ex)
            {
                return CacheVersion(gamePath, $"N/A - Error reading version: {ex.Message.Truncate(50)}");
            }
        }

        private string CacheVersion(string gamePath, string version)
        {
            if (gamePath == _cachedGamePath)
            {
                _cachedGameVersion = version;
            }
            return version;
        }

        private string? GetVersionFromFileMetadata(string gamePath)
        {
            foreach (var candidate in GameVersionFileCandidates)
            {
                var fullPath = Path.Combine(gamePath, candidate);
                var normalized = NormalizeGameVersion(ProbeFileVersion(fullPath));
                if (normalized != null)
                    return normalized;
            }
            return null;
        }

        private string? ProbeFileVersion(string filePath)
        {
            if (_fileVersionProbe != null)
                return _fileVersionProbe(filePath);

            try
            {
                if (!File.Exists(filePath))
                    return null;

                var info = FileVersionInfo.GetVersionInfo(filePath);
                return !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
            }
            catch
            {
                return null; // Unreadable candidate - try the next one.
            }
        }

        private static string? NormalizeGameVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Strip commit suffix / annotations: "1.7.4979.109+3371aa..." -> "1.7.4979.109",
            // "2022.3.35f1 (011206c7a712)" -> "2022.3.35f1".
            var versionPart = raw.Split(' ', '+')[0].Trim();
            var parts = versionPart.Split('.');
            if (parts.Length < 2)
                return null;
            if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
                return null;

            // Sanity guard: RimWorld game versions are 1.x-9.x. Anything else is an
            // engine/toolchain version (e.g. Unity's "2022.3.35f1") and must be rejected.
            if (major < 1 || major > 9)
                return null;

            return versionPart;
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
                return fullVersion; // Or return "N/A"? Let's return original for now.
            var secondPart = versionParts[1].Split(new[] { ' ', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return $"{versionParts[0]}.{secondPart}";
        }

        public void InvalidateCache()
        {
            // Reset resolution flag
            _isGamePathResolved = false;

_cachedGamePath = null;
            _cachedConfigPath = null;
            _cachedModsPath = null; 
            _cachedGameVersion = null;
            _cachedMajorGameVersion = null;
        }

        private void InvalidateModsPathCache()
        {
            _cachedModsPath = null;
        }

        public void RefreshPaths()
        {
            // Invalidate cache and reload paths
            InvalidateCache();

            var _ = GetGamePath();      // Resolves GamePath, potentially invalidates/resolves ModsPath cache
            var __ = GetModsPath();     // Resolves ModsPath based on potentially new GamePath
            var ___ = GetConfigPath();  // Resolves ConfigPath
            var ____ = GetGameVersion(); // Resolves Version based on potentially new GamePath
            var _____ = GetMajorGameVersion(); // Resolves MajorVersion
        }
    }

    internal static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}


