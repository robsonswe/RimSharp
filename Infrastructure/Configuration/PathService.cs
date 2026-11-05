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
        private readonly ILoggerService? _logger; // Optional: broken-override warnings (fail-open policy)
        private string? _cachedGamePath;
        private string? _cachedConfigPath;
        private string? _cachedModsPath; // Cache for resolved path
        private string? _cachedModsOverrideUsed; // mods_folder value backing _cachedModsPath (self-invalidation key)
        private string? _cachedModsGamePathUsed; // game path backing _cachedModsPath (self-invalidation key)
        private string? _cachedGameVersion;
        private string? _cachedMajorGameVersion;
        private bool _isGamePathResolved = false; // Flag to track if game path has been resolved

        public PathService(IConfigService configService)
            : this(configService, null, null)
        {
        }

        // Secondary constructor exists so unit tests can stub out file-version probing.
        public PathService(IConfigService configService, Func<string, string?>? fileVersionProbe)
            : this(configService, fileVersionProbe, null)
        {
        }

        // Full constructor: path resolution + optional logging for override warnings.
        public PathService(IConfigService configService, Func<string, string?>? fileVersionProbe, ILoggerService? logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _fileVersionProbe = fileVersionProbe;
            _logger = logger;
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
            var overridePath = _configService.GetConfigValue("mods_folder");
            var overrideKey = overridePath ?? string.Empty;

            // Self-invalidating cache: the entry is only honored while both inputs that
            // produced it are unchanged. This is what lets a mods_folder value written by
            // the settings dialog take effect on the next GetModsPath() call, without
            // requiring a restart or an explicit invalidation call (handoff doc §2.3).
            if (_cachedModsPath != null &&
                _cachedModsOverrideUsed == overrideKey &&
                _cachedModsGamePathUsed == gamePath)
            {
                return _cachedModsPath;
            }

            // Mod-folder override slot (handoff doc §2.3): if the user configured
            // mods_folder, it is honored while the directory exists; otherwise warn
            // and fall back to the derived path. Fail-open policy: a broken override
            // is bypassed but NEVER cleared — the stored value is preserved for the
            // moment the user fixes the path (deleting it would destroy user intent).
            if (TryResolveModsOverride(out var resolvedOverride, out var brokenOverride))
            {
                _cachedModsPath = resolvedOverride;
            }
            else
            {
                if (!string.IsNullOrEmpty(brokenOverride))
                {
                    WarnBrokenModsOverride(brokenOverride, gamePath);
                }

                if (string.IsNullOrEmpty(gamePath))
                {
                    _cachedModsPath = string.Empty; // No game path, no mods path
                }
                else
                {
                    string derivedModsPath = GetModsPathFor(gamePath);
                    _cachedModsPath = Directory.Exists(derivedModsPath) ? derivedModsPath : string.Empty;
                }
            }

            _cachedModsOverrideUsed = overrideKey;
            _cachedModsGamePathUsed = gamePath;
            return _cachedModsPath;
        }

        /// <summary>
        /// Resolves the local mods folder override slot for the *given* game folder.
        /// Precedence (handoff doc §2.3): a valid mods_folder override always wins;
        /// without one (or with a broken one — see GetModsPath for the warn+fallback
        /// path), the raw derivation rule applies. No existence check on the derived
        /// result and no cache — callers that need those use GetModsPath().
        /// This is the single writer rule for GameInstance.LocalFolder (handoff §6
        /// Phase 0): every writer of that property must go through this method or
        /// GetModsPath(), never a direct Path.Combine or a UI-model read.
        /// </summary>
        /// <param name="gamePath">The game installation folder to derive from.</param>
        /// <returns>The resolved mods folder path (may not exist on disk).</returns>
        public string GetModsPathFor(string gamePath)
        {
            if (TryResolveModsOverride(out var resolvedOverride, out _))
            {
                return resolvedOverride;
            }

            return string.IsNullOrEmpty(gamePath) ? string.Empty : Path.Combine(gamePath, "Mods");
        }

        /// <summary>
        /// Reads the mods_folder override slot. Out parameters disambiguate the two
        /// fail-states from "not configured": a broken override is returned via
        /// <paramref name="brokenOverride"/> so the caller can warn without deleting it.
        /// </summary>
        /// <returns>True when a valid override exists; false when unset or broken.</returns>
        private bool TryResolveModsOverride(out string resolvedOverride, out string brokenOverride)
        {
            resolvedOverride = string.Empty;
            brokenOverride = string.Empty;

            var configured = _configService.GetConfigValue("mods_folder");
            if (string.IsNullOrWhiteSpace(configured))
            {
                return false; // No override configured — the normal case
            }

            if (Directory.Exists(configured))
            {
                resolvedOverride = configured;
                return true;
            }

            brokenOverride = configured;
            return false;
        }

        private void WarnBrokenModsOverride(string brokenOverride, string gamePath)
        {
            string derivedPreview = string.IsNullOrEmpty(gamePath)
                ? "an empty mods path"
                : $"'{GetModsPathFor(gamePath)}'";
            string message =
                $"Mods folder override '{brokenOverride}' does not exist on disk. Falling back to {derivedPreview}. " +
                "The stored override has been kept — fix or clear it in Settings to stop seeing this warning.";

            if (_logger != null)
            {
                _logger.LogWarning(message, nameof(PathService));
            }
            else
            {
                // Test/no-logger construction: keep the signal without a logger dependency.
                Debug.WriteLine($"[PathService] {message}");
            }
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
            _cachedModsOverrideUsed = null;
            _cachedModsGamePathUsed = null;
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


