using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Configuration
{
    /// <summary>
    /// Service for detecting Steam installations and configuring game instances.
    /// Based on the researched community approach for Steam path autodetection.
    /// </summary>
    public class SteamDetectionService
    {
        private const string RimWorldAppId = "294100";
        private const string SteamAppworkshopAcf = "appworkshop_294100.acf";

        private readonly IPathService? _pathService;
        private readonly ILoggerService? _logger;

        /// <summary>
        /// IPathService is used for the single-writer rule on GameInstance.LocalFolder
        /// local-folder writes derive via IPathService.GetModsPathFor
        /// instead of a local Path.Combine. Optional so existing parameterless ctor callers
        /// keep working; the fallback keeps the pre-centralization behavior verbatim.
        /// ILoggerService is used for fail-open warnings on invalid overrides; optional for
        /// the same reason.
        /// This service is the single writer of GameInstance.WorkshopFolder.
        /// </summary>
        public SteamDetectionService(IPathService? pathService = null, ILoggerService? logger = null)
        {
            _pathService = pathService;
            _logger = logger;
        }

        /// <summary>
        /// Detect Steam installation and return a candidate.
        /// </summary>
        /// <returns>Steam installation candidate or null if not found</returns>
        public GameInstallationCandidate? DetectSteamInstallation()
        {
            try
            {
                // Try to find Steam installation
                var steamRoot = FindSteamRoot();
                if (steamRoot == null)
                    return null;

                // Try to find RimWorld in Steam libraries
                var gameFolder = FindRimWorldInSteam(steamRoot);
                if (gameFolder == null)
                    return null;

                // Get workshop folder
                var workshopFolder = GetWorkshopFolder(gameFolder, steamRoot);

                // Get config folder (Proton-aware on Linux)
                var configFolder = GetConfigFolder(steamRoot);

                return new GameInstallationCandidate
                {
                    Distribution = GameDistribution.Steam,
                    GameFolder = gameFolder,
                    ConfigFolder = configFolder ?? string.Empty,
                    WorkshopFolder = workshopFolder,
                    HasSteamClientIntegration = !string.IsNullOrEmpty(workshopFolder) && 
                                                Directory.Exists(workshopFolder) && 
                                                ValidateWorkshopAcfFile(workshopFolder),
                    DisplayName = $"Steam - {Path.GetFileName(gameFolder)}",
                    DetectionSource = "Steam VDF"
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Detect Steam installation and configure game instance (legacy method).
        /// </summary>
        /// <param name="instance">Game instance to configure</param>
        /// <returns>True if Steam installation was detected, false otherwise</returns>
        public bool DetectAndConfigureSteam(GameInstance instance)
        {
            // First, try to detect Steam installation
            var steamRoot = FindSteamRoot();
            if (steamRoot == null)
            {
                instance.IsSteamInstallation = false;
                return false;
            }

            // Try to find RimWorld in Steam libraries
            var gameFolder = FindRimWorldInSteam(steamRoot);
            if (gameFolder == null)
            {
                instance.IsSteamInstallation = false;
                return false;
            }

            instance.GameFolder = gameFolder;
            instance.IsSteamInstallation = true;
            instance.SteamClientIntegration = true;

            // Set workshop folder
            instance.WorkshopFolder = GetWorkshopFolder(gameFolder, steamRoot);

            // Set config folder based on platform (Proton-aware on Linux)
            instance.ConfigFolder = GetConfigFolder(steamRoot);

            // Set local folder: derivation rule lives in IPathService (single-writer rule
            // for GameInstance.LocalFolder). Overrides, when added,
            // flow through the same resolver. Falls back to the raw derivation if no
            // path service was provided (legacy parameterless construction).
            instance.LocalFolder = _pathService != null
                ? _pathService.GetModsPathFor(instance.GameFolder)
                : GetModsPathForFallback(instance.GameFolder);

            // Validate and fix any inconsistencies
            instance.ValidateAndFix();

            return instance.IsSteamInstallation;
        }

        /// <summary>
        /// Applies the persisted steam_workshop_folder override to the instance
        /// (fail-open policy — the detection service is the single
        /// writer of GameInstance.WorkshopFolder):
        ///   - empty/missing override ("auto mode"): keep the detected folder —
        ///     it is re-detected on every launch;
        ///   - set + valid (directory exists and appworkshop_294100.acf validates):
        ///     use the override;
        ///   - set + invalid: warn and fall back to the detected folder. The config
        ///     value is never cleared or deleted — user intent is preserved.
        /// GameInstance.ValidateAndFix() still runs afterwards as the consistency
        /// gate (it governs the Steam-claim state, not this slot).
        /// </summary>
        public void ApplyWorkshopFolderOverride(GameInstance instance, string? workshopOverride)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            // Empty = auto mode: no override, detection result stands (re-detected
            // every launch). Nothing to log — this is the normal case.
            if (string.IsNullOrWhiteSpace(workshopOverride))
            {
                return;
            }

            var overrideValid = Directory.Exists(workshopOverride) && ValidateWorkshopAcfFile(workshopOverride);
            if (overrideValid)
            {
                instance.WorkshopFolder = workshopOverride;
                _logger?.LogInfo(
                    $"Workshop folder override '{workshopOverride}' is valid (ACF file found). Using it.",
                    nameof(SteamDetectionService));
            }
            else
            {
                // Fail-open: keep the stored override in config; bypass it at runtime
                // and fall back to whatever detection resolved.
                var reason = Directory.Exists(workshopOverride)
                    ? "no appworkshop_294100.acf file found"
                    : "directory does not exist";
                var detected = instance.WorkshopFolder ?? string.Empty;
                _logger?.LogWarning(
                    $"Workshop folder override '{workshopOverride}' is invalid ({reason}). " +
                    $"Falling back to detected folder{(!string.IsNullOrEmpty(detected) ? $": {detected}" : " (none)")}. " +
                    "The saved override is kept in configuration.",
                    nameof(SteamDetectionService));
            }
        }

        /// <summary>
        /// Find Steam root directory.
        /// </summary>
        private string? FindSteamRoot()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return FindSteamRootWindows();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return FindSteamRootLinux();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return FindSteamRootMacOS();
            }

            return null;
        }

        /// <summary>
        /// Find Steam root on Windows using Registry (both WOW6432Node and standard) and common paths.
        /// Based on the researched community approach for Windows Steam detection.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private string? FindSteamRootWindows()
        {
            // 1. Check Registry (64-bit and 32-bit views in HKLM)
            string[] subKeys = new[]
            {
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };

            foreach (var subKey in subKeys)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                        {
                            return installPath;
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue with next key
                }
            }

            // Check HKCU
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    var steamPath = key.GetValue("SteamPath") as string ?? key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        steamPath = steamPath.Replace('/', Path.DirectorySeparatorChar);
                        if (Directory.Exists(steamPath))
                        {
                            return steamPath;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Continue with fallback
            }

            // Fallback to common locations
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    // Verify it's a valid Steam installation
                    if (File.Exists(Path.Combine(candidate, "steam.exe")) ||
                        Directory.Exists(Path.Combine(candidate, "steamapps")) ||
                        File.Exists(Path.Combine(candidate, "steamapps", "libraryfolders.vdf")) ||
                        File.Exists(Path.Combine(candidate, "config", "libraryfolders.vdf")))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Find Steam root on Linux.
        /// </summary>
        private string? FindSteamRootLinux()
        {
            // Shared candidate list (native/legacy roots first, then Flatpak, then
            // the snap-confined install); validity is checked per candidate below.
            var candidates = RimWorldDefaultPaths.GetLinuxSteamRootCandidates(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    if (Directory.Exists(Path.Combine(candidate, "steamapps")) ||
                        File.Exists(Path.Combine(candidate, "steamapps", "libraryfolders.vdf")) ||
                        File.Exists(Path.Combine(candidate, "config", "libraryfolders.vdf")))
                    {
                        LogSnapSteamRootIfNeeded(candidate);
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Logs a warning when the detected Linux Steam root is the snap-confined
        /// install. Snap packaging restricts filesystem/environment access enough
        /// that Steamworks.NET initialization can fail — at that point the ownership
        /// gate reports "unknown" and interactive Steam features stay disabled.
        /// This log is diagnostic context, never a gate.
        /// </summary>
        private void LogSnapSteamRootIfNeeded(string steamRoot)
        {
            var snapLeaf = Path.Combine("snap", "steam", "common", ".local", "share", "Steam");
            if (steamRoot.EndsWith(snapLeaf, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogWarning(
                    $"Snap Steam root detected ('{steamRoot}'). Steamworks.NET may fail to initialize under snap confinement; if Steam API init fails, subscription features will stay disabled (unknown ownership state).",
                    nameof(SteamDetectionService));
            }
        }

        /// <summary>
        /// Find Steam root on macOS.
        /// </summary>
        private string? FindSteamRootMacOS()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    if (Directory.Exists(Path.Combine(candidate, "steamapps")) ||
                        File.Exists(Path.Combine(candidate, "steamapps", "libraryfolders.vdf")) ||
                        File.Exists(Path.Combine(candidate, "config", "libraryfolders.vdf")))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Find RimWorld installation in Steam libraries.
        /// </summary>
        private string? FindRimWorldInSteam(string steamRoot)
        {
            var libraryFolders = ParseLibraryFolders(steamRoot);

            foreach (var library in libraryFolders)
            {
                var rimWorldPath = Path.Combine(library, "steamapps", "common", "RimWorld");
                if (IsValidRimWorldFolder(rimWorldPath))
                {
                    return rimWorldPath;
                }

                var rimWorldInRoot = Path.Combine(library, "RimWorld");
                if (IsValidRimWorldFolder(rimWorldInRoot))
                {
                    return rimWorldInRoot;
                }
            }

            var defaultCommon = Path.Combine(steamRoot, "steamapps", "common", "RimWorld");
            if (IsValidRimWorldFolder(defaultCommon))
            {
                return defaultCommon;
            }

            return null;
        }

        /// <summary>
        /// Parse Steam libraryfolders.vdf to get all library paths.
        /// </summary>
        private List<string> ParseLibraryFolders(string steamRoot)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };

            var candidates = new[]
            {
                Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                Path.Combine(steamRoot, "config", "libraryfolders.vdf")
            };

            string? libraryFoldersVdf = candidates.FirstOrDefault(File.Exists);
            if (libraryFoldersVdf == null)
            {
                return libraries.ToList();
            }

            try
            {
                var content = File.ReadAllText(libraryFoldersVdf);
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var rawPath = parts[parts.Length - 1].Replace(@"\\", @"\");
                            if (!string.IsNullOrEmpty(rawPath) && Directory.Exists(rawPath))
                            {
                                libraries.Add(rawPath);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Continue with whatever libraries were found
            }

            return libraries.ToList();
        }

        private string GetWorkshopFolder(string gameFolder, string steamRoot)
        {
            if (!string.IsNullOrEmpty(gameFolder))
            {
                var dir = new DirectoryInfo(gameFolder);
                while (dir != null)
                {
                    if (string.Equals(dir.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = Path.Combine(dir.FullName, "workshop", "content", RimWorldAppId);
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }

            return Path.Combine(steamRoot, "steamapps", "workshop", "content", RimWorldAppId);
        }

        /// <summary>
        /// Steam-aware config resolution. Returns the Proton prefix config folder when
        /// RimWorld was installed via Steam Play (Proton) — the game's Unity config
        /// then lives inside the compat prefix under the Steam root instead of the
        /// native Unity path — otherwise the native path. Non-Linux always resolves
        /// natively.
        /// </summary>
        private string GetConfigFolder(string? steamRoot)
        {
            var native = RimWorldDefaultPaths.GetConfigFolder() ?? GetNativeConfigFolder();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !string.IsNullOrWhiteSpace(steamRoot))
            {
                var protonConfig = RimWorldDefaultPaths.GetProtonConfigFolder(steamRoot);
                if (protonConfig != null)
                {
                    _logger?.LogInfo(
                        $"Proton prefix config detected: '{protonConfig}'. Using it instead of the native Unity config folder.",
                        nameof(SteamDetectionService));
                    return protonConfig;
                }
            }
            return native;
        }

        private string GetNativeConfigFolder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "unity3d", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorld", "Config");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorld", "Config");
        }

        public bool IsValidRimWorldFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return false;
            }

            var versionFile = Path.Combine(folderPath, "Version.txt");
            if (File.Exists(versionFile))
            {
                try
                {
                    var content = File.ReadAllText(versionFile);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Continue to next check
                }
            }

            var dataFolder = Path.Combine(folderPath, "Data");
            return Directory.Exists(dataFolder);
        }

        public bool IsRimWorldWorkshopFolder(string path)
        {
            return Path.GetFileName(path) == RimWorldAppId;
        }

        public bool ValidateWorkshopAcfFile(string workshopLocation)
        {
            try
            {
                if (string.IsNullOrEmpty(workshopLocation)) return false;

                var dir = new DirectoryInfo(workshopLocation);
                while (dir != null)
                {
                    var acfPath = Path.Combine(dir.FullName, SteamAppworkshopAcf);
                    if (File.Exists(acfPath))
                    {
                        return true;
                    }

                    if (string.Equals(dir.Name, "workshop", StringComparison.OrdinalIgnoreCase))
                    {
                        return File.Exists(Path.Combine(dir.FullName, SteamAppworkshopAcf));
                    }

                    dir = dir.Parent;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Fallback derivation for mods path when IPathService is not available (legacy parameterless construction).
        /// Bundle-aware on macOS: when gamePath ends with ".app" (case-insensitive, non-empty, directory),
        /// prefers <game>/Contents/Mods if it exists, else <game>/Mods.
        /// Non-bundle paths return Path.Combine(gamePath, "Mods") unchanged.
        /// Mirrors PathService.GetModsPathForCore logic to keep single-writer rule consistent.
        /// </summary>
        internal static string GetModsPathForFallback(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
                return string.Empty;

            // Check if this is a macOS .app bundle
            var fileName = Path.GetFileName(gamePath);
            if (!string.IsNullOrEmpty(fileName) &&
                fileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(gamePath))
            {
                // Bundle: prefer Contents/Mods, fallback to bundle-root Mods
                var contentsMods = Path.Combine(gamePath, "Contents", "Mods");
                if (Directory.Exists(contentsMods))
                    return contentsMods;

                var rootMods = Path.Combine(gamePath, "Mods");
                return rootMods;
            }

            // Non-bundle: legacy behavior (byte-identical)
            return Path.Combine(gamePath, "Mods");
        }
    }
}
