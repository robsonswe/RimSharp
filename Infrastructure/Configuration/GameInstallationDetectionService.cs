using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using RimSharp.Core.Services;
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Configuration
{
    public class GameInstallationDetectionService
    {
        private const string RimWorldSteamAppId = "294100";
        private const string RimWorldGogAppId1 = "1207658924";
        private const string RimWorldGogAppId2 = "1207658903";
        private const string SteamAppworkshopAcf = "appworkshop_294100.acf";

        /// <summary>
        /// Represents a scored config folder candidate.
        /// </summary>
        internal readonly struct ScoredConfigCandidate
        {
            public readonly string Path;
            public readonly int Score;
            public readonly DateTime ModsConfigLastWrite;
            public readonly int Ordinal; // For stable tie-breaking

            public ScoredConfigCandidate(string path, int score, DateTime modsConfigLastWrite, int ordinal)
            {
                Path = path;
                Score = score;
                ModsConfigLastWrite = modsConfigLastWrite;
                Ordinal = ordinal;
            }
        }

        /// <summary>
        /// Detects Steam RimWorld installation.
        /// Virtual for test stubbing (testable injection into SettingsDialogViewModel).
        /// </summary>
        public virtual GameInstallationCandidate? DetectSteamInstallation()
        {
            try
            {
                var steamRoot = FindSteamRoot();
                if (steamRoot == null) return null;

                var gameFolder = FindRimWorldInSteamLibrary(steamRoot);
                if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder))
                    return null;

                var workshopFolder = FindSteamWorkshopFolder(steamRoot);
                var configFolder = ResolveBestConfigFolder(steamRoot);

                return new GameInstallationCandidate
                {
                    Distribution = GameDistribution.Steam,
                    GameFolder = gameFolder,
                    ConfigFolder = configFolder,
                    WorkshopFolder = workshopFolder,
                    HasSteamClientIntegration = !string.IsNullOrEmpty(workshopFolder) &&
                                                Directory.Exists(workshopFolder) &&
                                                ValidateWorkshopAcfFile(workshopFolder),
                    DisplayName = $"Steam ({Path.GetFileName(gameFolder)})",
                    DetectionSource = "Steam Library"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Detects Non-Steam (GOG, Heroic, Standalone) RimWorld installation.
        /// Virtual for test stubbing (testable injection into SettingsDialogViewModel).
        /// </summary>
        public virtual GameInstallationCandidate? DetectNonSteamInstallation()
        {
            try
            {
                var nonSteamFolder = DetectNonSteamGameFolder();
                if (string.IsNullOrEmpty(nonSteamFolder) || !Directory.Exists(nonSteamFolder))
                    return null;

                var configFolder = ResolveBestConfigFolder(null);

                return new GameInstallationCandidate
                {
                    Distribution = GameDistribution.NonSteam,
                    GameFolder = nonSteamFolder,
                    ConfigFolder = configFolder,
                    WorkshopFolder = null,
                    HasSteamClientIntegration = false,
                    DisplayName = $"Non-Steam ({Path.GetFileName(nonSteamFolder)})",
                    DetectionSource = "GOG / Heroic / Standalone"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves the best config folder using scored candidate selection.
        /// Falls back to legacy behavior when no candidate scores above 0.
        /// </summary>
        internal string ResolveBestConfigFolder(string? steamRoot)
        {
            var candidates = EnumerateConfigCandidates(steamRoot).ToList();
            var scoredCandidates = ScoreCandidates(candidates).ToList();

            if (scoredCandidates.Count == 0)
            {
                return ResolveConfigFolderForSteamRoot(steamRoot);
            }

            var best = scoredCandidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.ModsConfigLastWrite.Ticks)
                .ThenBy(c => c.Path, StringComparer.Ordinal)
                .ThenBy(c => c.Ordinal)
                .First();

            // If best candidate scores 0, fall back to legacy behavior
            if (best.Score <= 0)
            {
                return ResolveConfigFolderForSteamRoot(steamRoot);
            }

            return best.Path;
        }

        /// <summary>
        /// Enumerates all config folder candidates for the current platform.
        /// Exception-safe: any IO errors are caught and ignored per candidate.
        /// </summary>
        protected virtual IEnumerable<string> EnumerateConfigCandidates(string? steamRoot)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var results = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: children of %LOCALAPPDATA%\Low\Ludeon Studios\ whose name contains "rimworld"
                var ludeonRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Low", "Ludeon Studios");

                if (Directory.Exists(ludeonRoot))
                {
                    try
                    {
                        foreach (var productDir in Directory.GetDirectories(ludeonRoot))
                        {
                            var productName = Path.GetFileName(productDir);
                            if (productName.IndexOf("rimworld", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var configPath = Path.Combine(productDir, "Config");
                                if (Directory.Exists(configPath))
                                {
                                    results.Add(configPath);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Also include the default config folder
                results.Add(GetDefaultConfigFolder());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: children of ~/.config/unity3d/Ludeon Studios/ containing "rimworld"
                var unity3dRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "unity3d", "Ludeon Studios");

                if (Directory.Exists(unity3dRoot))
                {
                    try
                    {
                        foreach (var productDir in Directory.GetDirectories(unity3dRoot))
                        {
                            var productName = Path.GetFileName(productDir);
                            if (productName.IndexOf("rimworld", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var configPath = Path.Combine(productDir, "Config");
                                if (Directory.Exists(configPath))
                                {
                                    results.Add(configPath);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Also include the default config folder (fixed-name product)
                results.Add(GetDefaultConfigFolder());

                // Proton candidates (all compatdata appids, all users)
                if (!string.IsNullOrWhiteSpace(steamRoot))
                {
                    foreach (var protonConfig in RimWorldDefaultPaths.EnumerateProtonConfigCandidates(steamRoot))
                    {
                        results.Add(protonConfig);
                    }
                }

                // Wine prefix candidates
                foreach (var wineConfig in RimWorldDefaultPaths.EnumerateWineConfigCandidates(userProfile))
                {
                    results.Add(wineConfig);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: enumerate children of ~/Library/Application Support whose name contains "rimworld"
                var appSupportRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
                // On macOS, ApplicationData maps to ~/Library/Application Support

                if (Directory.Exists(appSupportRoot))
                {
                    try
                    {
                        foreach (var childDir in Directory.GetDirectories(appSupportRoot))
                        {
                            var childName = Path.GetFileName(childDir);
                            if (childName.IndexOf("rimworld", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var configPath = Path.Combine(childDir, "Config");
                                if (Directory.Exists(configPath))
                                {
                                    results.Add(configPath);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Also include the explicit default candidate
                results.Add(GetDefaultConfigFolder());
            }

            foreach (var result in results)
            {
                yield return result;
            }
        }

        /// <summary>
        /// Scores each config folder candidate based on heuristics.
        /// </summary>
        internal IEnumerable<ScoredConfigCandidate> ScoreCandidates(IEnumerable<string> candidates)
        {
            int ordinal = 0;
            var now = DateTime.UtcNow;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                {
                    ordinal++;
                    continue;
                }

                int score = 0;
                DateTime modsConfigLastWrite = DateTime.MinValue;

                try
                {
                    var modsConfigPath = Path.Combine(candidate, "ModsConfig.xml");
                    if (File.Exists(modsConfigPath))
                    {
                        score += 100;
                        modsConfigLastWrite = File.GetLastWriteTimeUtc(modsConfigPath);

                        var age = now - modsConfigLastWrite;
                        if (age.TotalDays < 1)
                            score += 50;
                        else if (age.TotalDays < 7)
                            score += 30;
                        else if (age.TotalDays < 30)
                            score += 10;
                    }

                    // Saves folder bonus
                    var savesPath = Path.Combine(candidate, "Saves");
                    if (Directory.Exists(savesPath))
                    {
                        try
                        {
                            var saveFiles = Directory.GetFiles(savesPath, "*.rws");
                            if (saveFiles.Length > 0)
                            {
                                score += 20 + Math.Min(saveFiles.Length, 10);
                            }
                        }
                        catch { }
                    }

                    // steamuser folder bonus (+5)
                    // Check if the Config folder is under a user folder named "steamuser"
                    // Path structure: .../users/steamuser/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config
                    var configParent = Directory.GetParent(candidate);
                    if (configParent != null)
                    {
                        // Go up: Config -> Product -> Company -> LocalLow -> AppData -> User (steamuser)
                        var productFolder = configParent.Parent;           // RimWorld by Ludeon Studios
                        var companyFolder = productFolder?.Parent;         // Ludeon Studios
                        var localLowFolder = companyFolder?.Parent;        // LocalLow
                        var appDataFolder = localLowFolder?.Parent;        // AppData
                        var userFolder = appDataFolder;                    // steamuser (or other username) - this IS the user folder
                        
                        // userFolder is the actual user directory (e.g., "steamuser")
                        if (userFolder != null)
                        {
                            var userName = userFolder.Name;
                            if (string.Equals(userName, "steamuser", StringComparison.OrdinalIgnoreCase))
                            {
                                score += 5;
                            }
                        }
                    }
                }
                catch
                {
                    // Exception-safe scoring: any error results in score 0
                }

                yield return new ScoredConfigCandidate(candidate, score, modsConfigLastWrite, ordinal);
                ordinal++;
            }
        }

        /// <summary>
        /// Structurally validates a user's manual "this is a Steam RimWorld" claim for
        /// the given game directory (trust-ladder rung 2). Falls well short
        /// of proving provenance — it verifies configuration consistency:
        ///   1. the directory exists and looks like RimWorld (IsRimWorldInstallation),
        ///   2. it sits directly under <library>\steamapps\common\ (Steam's own fixed
        ///      vocabulary — deliberately NOT hardcoding the game's folder name: RimWorld
        ///      1.7/Waylith already reshaped the install, and the executables/installdir
        ///      are Ludeon-owned and mutable),
        ///   3. the owning steamapps carries appmanifest_294100.acf and (when present)
        ///      its "installdir" field matches the claimed folder name — real Steam
        ///      installs keep manifests; DRM-free folders do not,
        ///   4. the resolved workshop folder validates (appworkshop_294100.acf present).
        /// On success, WorkshopFolder carries the resolved content folder when it exists;
        /// null means "no workshop content downloaded yet" (caller persists auto-detect).
        /// A failed claim must be refused persistence upstream with FailureReason shown.
        /// </summary>
        public SteamClaimValidationResult ValidateSteamClaim(string? gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                return SteamClaimValidationResult.Fail("No game directory set. Select the folder where RimWorld is installed.");
            }

            string fullGamePath;
            try
            {
                fullGamePath = Path.GetFullPath(gamePath.TrimEnd('\\', '/'));
            }
            catch
            {
                return SteamClaimValidationResult.Fail($"'{gamePath}' is not a valid directory path.");
            }

            if (!Directory.Exists(fullGamePath))
            {
                return SteamClaimValidationResult.Fail($"The game directory '{fullGamePath}' does not exist.");
            }

            if (!IsRimWorldInstallation(fullGamePath))
            {
                return SteamClaimValidationResult.Fail(
                    $"'{fullGamePath}' does not look like a RimWorld installation " +
                    "(RimWorld executable plus Version.txt/Data not found).");
            }

            // Steam layout anchors on Steam-owned vocabulary only: <game>\..\common\steamapps
            var commonParent = Path.GetDirectoryName(fullGamePath);
            var steamappsGrandParent = commonParent == null ? null : Path.GetDirectoryName(commonParent);
            var commonFolderName = Path.GetFileName(commonParent ?? string.Empty);
            var steamappsFolderName = Path.GetFileName(steamappsGrandParent ?? string.Empty);

            // 3-5: every Steam-specific check requires locating this install's steamapps folder
            if (!string.Equals(commonFolderName, "common", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(steamappsFolderName, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return SteamClaimValidationResult.Fail(
                    $"'{fullGamePath}' is not located directly under a 'steamapps\\common\\' Steam library path.");
            }

            var steamapps = steamappsGrandParent!;
            var manifestPath = Path.Combine(steamapps, "appmanifest_294100.acf");
            if (!File.Exists(manifestPath))
            {
                return SteamClaimValidationResult.Fail(
                    $"No appmanifest_294100.acf was found next to the game (expected in '{steamapps}'). " +
                    "This folder is not managed by the Steam client.");
            }

            // The manifest's own "installdir" record names the folder Steam installs into —
            // match against the claim instead of a hardcoded "RimWorld" folder name.
            var installdir = ParseManifestInstalldir(manifestPath);
            if (!string.IsNullOrEmpty(installdir) &&
                !string.Equals(installdir, Path.GetFileName(fullGamePath), StringComparison.OrdinalIgnoreCase))
            {
                return SteamClaimValidationResult.Fail(
                    $"The Steam manifest this library lists installdir '{installdir}', which does not match the claimed folder '{Path.GetFileName(fullGamePath)}'.");
            }

            var workshopContent = Path.Combine(steamapps, "workshop", "content", RimWorldSteamAppId);
            if (!ValidateWorkshopAcfFile(workshopContent))
            {
                return SteamClaimValidationResult.Fail(
                    $"The workshop folder for this library does not validate (no appworkshop_294100.acf under '{Path.Combine(steamapps, "workshop")}'). " +
                    "If this is a fresh Steam install, download at least one Workshop mod once, then try again.");
            }

            var resolvedWorkshop = Directory.Exists(workshopContent) ? workshopContent : null;
            return SteamClaimValidationResult.Success(resolvedWorkshop);
        }

        /// <summary>
        /// Reads the "installdir" value from an appmanifest .acf file (plain key-value
        /// text: line-shaped `"installdir"    "RimWorld"`). Returns null when absent or
        /// unreadable — validation stays structural rather than parser-fragile.
        /// </summary>
        private static string? ParseManifestInstalldir(string manifestPath)
        {
            try
            {
                foreach (var rawLine in File.ReadAllLines(manifestPath))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("\"installdir\"", StringComparison.OrdinalIgnoreCase))
                    {
                        // Same split style as ParseLibraryFolders: it reliably returns the
                        // last quoted token of the line ("installdir"    "RimWorld")
                        var parts = line.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            return parts[parts.Length - 1].Trim();
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        internal static string GetDefaultConfigFolder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "unity3d", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorld", "Config");
        }

        /// <summary>
        /// Steam-aware config resolution. On Linux, RimWorld running through Proton
        /// writes its config inside the compat prefix under the Steam root
        /// (<c>steamapps/compatdata/294100/pfx/drive_c/users/steamuser/...</c>);
        /// the native <c>~/.config/unity3d/...</c> path is then unused by the game.
        /// When the prefix config exists it wins; otherwise the native path stands.
        /// Non-Linux (and null/unrelated steamRoot) always resolves natively.
        /// </summary>
        internal static string ResolveConfigFolderForSteamRoot(string? steamRoot)
        {
            var native = RimWorldDefaultPaths.GetConfigFolder() ?? GetDefaultConfigFolder();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !string.IsNullOrWhiteSpace(steamRoot))
            {
                var protonConfig = RimWorldDefaultPaths.GetProtonConfigFolder(steamRoot);
                if (protonConfig != null)
                {
                    return protonConfig;
                }
            }
            return native;
        }

        private string? DetectNonSteamGameFolder()
        {
            // 1. Windows GOG Registry
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string[] registryKeys = new[]
                {
                    @"SOFTWARE\WOW6432Node\GOG.com\Games",
                    @"SOFTWARE\GOG.com\Games"
                };

                foreach (var regKeyPath in registryKeys)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(regKeyPath);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var gameName = subKey.GetValue("gameName") as string
                                        ?? subKey.GetValue("GAMENAME") as string
                                        ?? string.Empty;
                            var gameId = subKey.GetValue("gameID") as string
                                      ?? subKey.GetValue("GAMEID") as string
                                      ?? subKeyName;
                            var exe = subKey.GetValue("exe") as string
                                   ?? subKey.GetValue("EXE") as string
                                   ?? string.Empty;
                            var installPath = subKey.GetValue("path") as string
                                           ?? subKey.GetValue("PATH") as string
                                           ?? string.Empty;

                            if (string.IsNullOrEmpty(installPath) && !string.IsNullOrEmpty(exe))
                            {
                                installPath = Path.GetDirectoryName(exe) ?? string.Empty;
                            }

                            bool metadataIsRimWorld = gameName.IndexOf("RimWorld", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      exe.IndexOf("RimWorld", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      gameId == RimWorldGogAppId1 || gameId == RimWorldGogAppId2;

                            if (metadataIsRimWorld && !string.IsNullOrEmpty(installPath) && IsRimWorldInstallation(installPath))
                            {
                                return installPath;
                            }
                        }
                    }
                    catch { }
                }
            }

            // 2. Heroic Games Launcher metadata
            var heroicFolder = DetectHeroicInstallation();
            if (!string.IsNullOrEmpty(heroicFolder))
            {
                return heroicFolder;
            }

            // 3. Platform common installation directories
            var searchPaths = new List<string>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                searchPaths.Add(@"C:\GOG Games\RimWorld");
                searchPaths.Add(Path.Combine(userProfile, "GOG Games", "RimWorld"));
                searchPaths.Add(Path.Combine(progFiles86, "GOG Galaxy", "Games", "RimWorld"));
                searchPaths.Add(Path.Combine(progFiles, "GOG Galaxy", "Games", "RimWorld"));
                searchPaths.Add(Path.Combine(userProfile, "Games", "RimWorld"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                searchPaths.Add(Path.Combine(userProfile, "GOG Games", "RimWorld", "game"));
                searchPaths.Add(Path.Combine(userProfile, "GOG Games", "RimWorld"));
                searchPaths.Add(Path.Combine(userProfile, "Games", "Heroic", "RimWorld"));
                searchPaths.Add(Path.Combine(userProfile, "Games", "GOG", "RimWorld"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                searchPaths.Add("/Applications/RimWorld.app");
                searchPaths.Add(Path.Combine(userProfile, "Applications", "RimWorld.app"));
            }

            foreach (var candidate in searchPaths)
            {
                if (IsRimWorldInstallation(candidate))
                {
                    return candidate;
                }
            }

            // macOS: Scan /Applications and ~/Applications for RimWorld*.app bundles
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var appScanResult = ScanMacOSApplicationsForRimWorld();
                if (!string.IsNullOrEmpty(appScanResult))
                {
                    return appScanResult;
                }
            }

            // 4. Technique 2: Filtered drive-scan fallback (Windows only, last resort)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var driveScanResult = ScanDrivesForRimWorldGameFolder();
                if (!string.IsNullOrEmpty(driveScanResult))
                {
                    return driveScanResult;
                }
            }

            return null;
        }

        /// <summary>
        /// Scans /Applications and ~/Applications for RimWorld*.app bundles on macOS.
        /// Validates each with IsRimWorldMacAppBundle. First valid hit wins.
        /// Exception-safe per candidate. Uses injected roots for testability.
        /// </summary>
        internal string? ScanMacOSApplicationsForRimWorld(
            IEnumerable<string>? appRoots = null,
            Func<string, IEnumerable<string>>? directoryLister = null)
        {
            var roots = appRoots ?? GetMacOSApplicationRoots();
            var lister = directoryLister ?? ListDirectoriesSafe;

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                IEnumerable<string> subDirs;
                try
                {
                    subDirs = lister(root);
                }
                catch
                {
                    continue;
                }

                var rimWorldApps = subDirs
                    .Where(d =>
                    {
                        try
                        {
                            var name = Path.GetFileName(d);
                            return name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                                   name.StartsWith("RimWorld", StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .OrderBy(d =>
                    {
                        var name = Path.GetFileName(d);
                        if (string.Equals(name, "RimWorld.app", StringComparison.OrdinalIgnoreCase)) return 0;
                        if (string.Equals(name, "RimWorldMac.app", StringComparison.OrdinalIgnoreCase)) return 1;
                        return 2;
                    })
                    .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var app in rimWorldApps)
                {
                    if (IsRimWorldMacAppBundle(app))
                    {
                        return app;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the macOS application roots to scan. Virtual for test injection.
        /// </summary>
        protected virtual IEnumerable<string> GetMacOSApplicationRoots()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                "/Applications",
                Path.Combine(userProfile, "Applications")
            };
        }

        /// <summary>
        /// Technique 2: Filtered drive-scan fallback for RimWorld game folder.
        /// Windows only. Enumerates fixed drives (C: through E: plus other fixed drives, max 6 total),
        /// recursive search max depth 3. Skips system directories, descends only into known game-library
        /// directory names. Returns first match (first-match-wins). Exception-safe per directory.
        /// Virtual for test stubbing; the real-drive enumeration is in a thin public wrapper.
        /// </summary>
        protected virtual string? ScanDrivesForRimWorldGameFolder()
        {
            // Real-drive enumeration wrapper
            var roots = GetFixedDriveRootsForScan();
            return ScanDrivesForRimWorldGameFolderInternal(roots, ListDirectoriesSafe);
        }

        /// <summary>
        /// Internal testable seam for drive-scan. Accepts root paths and a directory lister
        /// so tests can drive a synthetic tree without touching real drives.
        /// </summary>
        internal string? ScanDrivesForRimWorldGameFolderInternal(
            IEnumerable<string> rootPaths,
            Func<string, IEnumerable<string>>? directoryLister = null)
        {
            var lister = directoryLister ?? ListDirectoriesSafe;
            const int maxDepth = 3;

            // Skip-list: directories to never enter (case-insensitive)
            var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "windows",
                "program files",
                "program files (x86)",
                "appdata",
                "users",
                "$recycle.bin",
                "system volume information",
                "unity",
                "steamapps"
            };

            // Descend-list: only enter these directory names (case-insensitive)
            var descendDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "games",
                "steam",
                "steam library",
                "steamlibrary",
                "steam libraries",
                "gog games",
                "epic games",
                "program files",
                "program files (x86)",
                "steamapps",
                "common",
                "games library"
            };

            foreach (var root in rootPaths)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                var queue = new Queue<(string Path, int Depth)>();
                queue.Enqueue((root, 0));

                while (queue.Count > 0)
                {
                    var (currentPath, depth) = queue.Dequeue();

                    if (depth > maxDepth)
                        continue;

                    IEnumerable<string> subDirs;
                    try
                    {
                        subDirs = lister(currentPath);
                    }
                    catch
                    {
                        continue; // Exception-safe: skip this directory
                    }

                    foreach (var dir in subDirs)
                    {
                        string dirName;
                        try
                        {
                            dirName = Path.GetFileName(dir);
                        }
                        catch
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(dirName) || dirName.StartsWith("."))
                            continue;

                        // Check if this directory looks like a RimWorld installation
                        if (dirName.IndexOf("rimworld", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (IsRimWorldInstallation(dir))
                            {
                                return dir; // First-match-wins
                            }
                            // Even if not a valid installation, don't descend further into it
                            continue;
                        }

                        // Only descend into directories in the descend-list
                        if (descendDirs.Contains(dirName) && !skipDirs.Contains(dirName))
                        {
                            if (depth < maxDepth)
                            {
                                queue.Enqueue((dir, depth + 1));
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets fixed drive roots for scanning: C: through E: first, then any other fixed drives alphabetically.
        /// Capped at 6 drives total.
        /// </summary>
        private static IEnumerable<string> GetFixedDriveRootsForScan()
        {
            var roots = new List<string>();
            var priorityDrives = new[] { "C:", "D:", "E:" };

            foreach (var driveLetter in priorityDrives)
            {
                var root = driveLetter + "\\";
                if (Directory.Exists(root))
                {
                    roots.Add(root);
                }
            }

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (roots.Count >= 6)
                        break;

                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        var root = drive.RootDirectory.FullName;
                        if (!roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                        {
                            roots.Add(root);
                        }
                    }
                }
            }
            catch { }

            return roots;
        }

        /// <summary>
        /// Exception-safe directory enumeration.
        /// </summary>
        private static IEnumerable<string> ListDirectoriesSafe(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private string? DetectHeroicInstallation()
        {
            try
            {
                string installedJsonPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gog_store", "installed.json")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "heroic", "gog_store", "installed.json");

                if (!File.Exists(installedJsonPath)) return null;

                var json = File.ReadAllText(installedJsonPath);
                using var doc = JsonDocument.Parse(json);

                JsonElement listElement;
                if (doc.RootElement.TryGetProperty("installed", out var prop) && prop.ValueKind == JsonValueKind.Array)
                {
                    listElement = prop;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    listElement = doc.RootElement;
                }
                else
                {
                    return null;
                }

                foreach (var item in listElement.EnumerateArray())
                {
                    string appName = item.TryGetProperty("appName", out var appNameProp) ? appNameProp.GetString() ?? "" : "";
                    string installPath = item.TryGetProperty("install_path", out var pathProp) ? pathProp.GetString() ?? "" : "";

                    if ((appName == RimWorldGogAppId1 || appName == RimWorldGogAppId2 || appName.Contains("RimWorld", StringComparison.OrdinalIgnoreCase))
                        && IsRimWorldInstallation(installPath))
                    {
                        return installPath;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Strictly validates that a path is a RimWorld installation.
        /// The executable is the primary identity marker (kept as required for the
        /// version.txt path) — file names are corroboration only. The exe list covers
        /// both current layouts: RimWorld.exe (1.7 Waylith, self-contained .NET runtime)
        /// and RimWorldWin64.exe (Unity-era 1.5). Exe names are the most mutable signal,
        /// so the list may need a new entry if a future version renames the binary again.
        /// On macOS, .app bundles are validated via IsRimWorldMacAppBundle.
        /// </summary>
        public bool IsRimWorldInstallation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            try
            {
                // On macOS, if the path is an .app bundle, use the specialized validator
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                    Path.GetFileName(path).EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    return IsRimWorldMacAppBundle(path);
                }

                // 1. Executables — the primary marker
                string[] executables = new[]
                {
                    "RimWorldWin64.exe", "RimWorldWin.exe", "RimWorld.exe",
                    "RimWorldLinux64", "RimWorldLinux", "RimWorld",
                    "RimWorld.app", "RimWorldMac.app"
                };

                bool hasExecutable = executables.Any(exe =>
                    File.Exists(Path.Combine(path, exe)) || Directory.Exists(Path.Combine(path, exe)));

                // 2. Official Core Mod About.xml (<packageId>ludeon.rimworld</packageId>) —
                //    strongest corroboration; ships in Data\Core\About\About.xml in the
                //    Waylith layout as well.
                string coreAboutXml = ModAboutPaths.GetAboutFilePath(Path.Combine(path, "Data", "Core"));
                bool hasCoreAbout = false;
                if (File.Exists(coreAboutXml))
                {
                    var content = File.ReadAllText(coreAboutXml);
                    if (content.IndexOf("ludeon.rimworld", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        content.IndexOf("<name>Core</name>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        content.IndexOf("Ludeon Studios", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasCoreAbout = true;
                    }
                }

                // 3. Parsable RimWorld Version.txt (e.g. "1.5.4069 rev1234")
                string versionFile = Path.Combine(path, "Version.txt");
                bool hasRimWorldVersion = false;
                if (File.Exists(versionFile))
                {
                    var firstLine = File.ReadLines(versionFile).FirstOrDefault()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(firstLine) && char.IsDigit(firstLine[0]) && firstLine.Contains('.'))
                    {
                        hasRimWorldVersion = true;
                    }
                }

                return hasCoreAbout || (hasExecutable && hasRimWorldVersion);
            }
            catch
            {
                return false;
            }
        }

        private string? FindSteamRoot()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                                 ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                        {
                            return installPath;
                        }
                    }

                    // Technique 1: HKCU Steam registry hive fallback
                    try
                    {
                        using var hkcuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                        if (hkcuKey != null)
                        {
                            var installPath = hkcuKey.GetValue("SteamPath") as string;
                            if (!string.IsNullOrEmpty(installPath))
                            {
                                // Normalize separators (value can contain forward slashes)
                                var normalizedPath = installPath.Replace('/', '\\');
                                if (Directory.Exists(normalizedPath))
                                {
                                    return normalizedPath;
                                }
                            }
                        }
                    }
                    catch { }

                    var commonPaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
                    };

                    return commonPaths.FirstOrDefault(Directory.Exists);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return RimWorldDefaultPaths
                        .GetLinuxSteamRootCandidates(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                        .FirstOrDefault(Directory.Exists);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return FindMacOSSteamRoot();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Finds the Steam root on macOS. Returns the path only if it exists AND looks like Steam
        /// (contains a "steamapps" directory OR config/libraryfolders.vdf OR a "steam.sh" binary).
        /// </summary>
        internal string? FindMacOSSteamRoot()
        {
            var macPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam");
            if (!Directory.Exists(macPath))
                return null;

            // Validate it looks like a Steam installation
            var steamApps = Path.Combine(macPath, "steamapps");
            var libraryFoldersVdf = Path.Combine(macPath, "config", "libraryfolders.vdf");
            var steamSh = Path.Combine(macPath, "steam.sh");

            if (Directory.Exists(steamApps) || File.Exists(libraryFoldersVdf) || File.Exists(steamSh))
            {
                return macPath;
            }

            return null;
        }

        private string? FindRimWorldInSteamLibrary(string steamRoot)
        {
            try
            {
                var mainSteamApps = Path.Combine(steamRoot, "steamapps");
                if (!Directory.Exists(mainSteamApps)) return null;

                // On macOS, the game is an .app bundle inside the common folder
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return FindRimWorldMacAppInSteamLibrary(mainSteamApps);
                }

                var defaultGame = Path.Combine(mainSteamApps, "common", "RimWorld");
                if (IsRimWorldInstallation(defaultGame)) return defaultGame;

                var libraryFolders = Path.Combine(mainSteamApps, "libraryfolders.vdf");
                List<string>? libraryPaths = null;

                // Manifest-installdir fallback: appmanifest_294100.acf records where the
                // game actually lives, so detection survives an installdir rename the same
                // way it survives new library drives. Avoids the RimPy failure mode where
                // hardcoded install-folder/exe names rot when Ludeon reshapes the install.
                // Runs only after the plain name scan, so current behavior is preserved.
                var viaMainManifest = TryFindRimWorldViaManifest(mainSteamApps);
                if (!string.IsNullOrEmpty(viaMainManifest)) return viaMainManifest;

                if (File.Exists(libraryFolders))
                {
                    libraryPaths = ParseLibraryFolders(libraryFolders);
                    foreach (var libPath in libraryPaths)
                    {
                        var libGame = Path.Combine(libPath, "steamapps", "common", "RimWorld");
                        if (IsRimWorldInstallation(libGame))
                        {
                            return libGame;
                        }
                    }

                    foreach (var libPath in libraryPaths)
                    {
                        var viaLibManifest = TryFindRimWorldViaManifest(Path.Combine(libPath, "steamapps"));
                        if (!string.IsNullOrEmpty(viaLibManifest)) return viaLibManifest;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Finds RimWorld .app bundle in Steam libraries on macOS.
        /// Enumerates libraries via libraryfolders.vdf and also considers the main steamapps.
        /// For each library, looks for .app bundles in steamapps/common/RimWorld.
        /// </summary>
        internal string? FindRimWorldMacAppInSteamLibrary(string mainSteamApps)
        {
            var libraryPaths = new List<string>();

            // Always consider the main steamapps as a library
            libraryPaths.Add(Path.GetDirectoryName(mainSteamApps) ?? string.Empty);

            // Parse additional libraries from libraryfolders.vdf
            var libraryFoldersVdf = Path.Combine(Path.GetDirectoryName(mainSteamApps) ?? string.Empty, "config", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersVdf))
            {
                var parsed = ParseLibraryFolders(libraryFoldersVdf);
                libraryPaths.AddRange(parsed);
            }

            foreach (var libPath in libraryPaths)
            {
                if (string.IsNullOrEmpty(libPath) || !Directory.Exists(libPath))
                    continue;

                var commonPath = Path.Combine(libPath, "steamapps", "common", "RimWorld");
                if (!Directory.Exists(commonPath))
                    continue;

                // Look for .app bundles inside the common/RimWorld directory
                var appBundle = FindRimWorldAppBundle(commonPath);
                if (!string.IsNullOrEmpty(appBundle))
                {
                    return appBundle;
                }

                // Fallback: check for RimWorldMac.app directly
                var fallbackApp = Path.Combine(commonPath, "RimWorldMac.app");
                if (Directory.Exists(fallbackApp) && IsRimWorldMacAppBundle(fallbackApp))
                {
                    return fallbackApp;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a RimWorld .app bundle in the given directory.
        /// Enumerates immediate children ending with ".app" (case-insensitive).
        /// Prefers "RimWorld.app", then "RimWorldMac.app", then alphabetical.
        /// </summary>
        internal string? FindRimWorldAppBundle(string searchDir)
        {
            try
            {
                var appDirs = Directory.GetDirectories(searchDir, "*.app", SearchOption.TopDirectoryOnly)
                    .Where(d => IsRimWorldMacAppBundle(d))
                    .ToList();

                if (appDirs.Count == 0)
                    return null;

                // Prefer RimWorld.app, then RimWorldMac.app, then alphabetical
                var preferred = appDirs.FirstOrDefault(d => string.Equals(Path.GetFileName(d), "RimWorld.app", StringComparison.OrdinalIgnoreCase));
                if (preferred != null) return preferred;

                preferred = appDirs.FirstOrDefault(d => string.Equals(Path.GetFileName(d), "RimWorldMac.app", StringComparison.OrdinalIgnoreCase));
                if (preferred != null) return preferred;

                return appDirs.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).First();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates that a path is a RimWorld .app bundle on macOS.
        /// Returns true when: directory name ends with ".app", contains a non-symlink "Contents" directory,
        /// AND (a) "Contents/Version.txt" exists, OR (b) "Contents/Data" exists, OR (c) bundle-root "Data" exists,
        /// OR (d) "Contents/Resources/Data" exists. Each probe is exception-safe.
        /// </summary>
        internal bool IsRimWorldMacAppBundle(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            try
            {
                var dirName = Path.GetFileName(path);
                if (!dirName.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    return false;

                var contentsPath = Path.Combine(path, "Contents");
                if (!Directory.Exists(contentsPath))
                    return false;

                // Check if Contents is a symlink (should be a real directory)
                var contentsInfo = new DirectoryInfo(contentsPath);
                if ((contentsInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;

                // Check for Version.txt in Contents
                if (File.Exists(Path.Combine(contentsPath, "Version.txt")))
                    return true;

                // Check for Contents/Data
                if (Directory.Exists(Path.Combine(contentsPath, "Data")))
                    return true;

                // Check for Data at bundle root
                if (Directory.Exists(Path.Combine(path, "Data")))
                    return true;

                // Check for Contents/Resources/Data
                if (Directory.Exists(Path.Combine(contentsPath, "Resources", "Data")))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads <steamapps>\appmanifest_294100.acf, resolves the recorded "installdir"
        /// and returns the game folder when it is a genuine RimWorld installation.
        /// Public for testability (name-based detection hardcodes "RimWorld"; this is the
        /// rename-tolerant fallback). Returns null when the manifest is absent, unreadable,
        /// lacks installdir, or the recorded folder is not RimWorld.
        /// </summary>
        public string? TryFindRimWorldViaManifest(string? steamappsDir)
        {
            try
            {
                if (string.IsNullOrEmpty(steamappsDir) || !Directory.Exists(steamappsDir))
                {
                    return null;
                }

                var manifestPath = Path.Combine(steamappsDir, "appmanifest_294100.acf");
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                var installdir = ParseManifestInstalldir(manifestPath);
                if (string.IsNullOrEmpty(installdir))
                {
                    return null;
                }

                var gameFolder = Path.Combine(steamappsDir, "common", installdir);
                return IsRimWorldInstallation(gameFolder) ? gameFolder : null;
            }
            catch
            {
                return null;
            }
        }

        private string? FindSteamWorkshopFolder(string steamRoot)
        {
            try
            {
                var workshopPath = Path.Combine(steamRoot, "steamapps", "workshop", "content", RimWorldSteamAppId);
                return Directory.Exists(workshopPath) ? workshopPath : null;
            }
            catch
            {
                return null;
            }
        }

        private List<string> ParseLibraryFolders(string libraryFoldersPath)
        {
            var paths = new List<string>();
            try
            {
                foreach (var rawLine in File.ReadAllLines(libraryFoldersPath))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { '"' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var rawPath = parts[parts.Length - 1].Replace(@"\\", @"\");
                            if (!string.IsNullOrEmpty(rawPath) && Directory.Exists(rawPath))
                            {
                                paths.Add(rawPath);
                            }
                        }
                    }
                }
            }
            catch { }
            return paths;
        }

        private bool ValidateWorkshopAcfFile(string workshopLocation)
        {
            try
            {
                if (string.IsNullOrEmpty(workshopLocation) || !Directory.Exists(workshopLocation))
                    return false;

                var dir = new DirectoryInfo(workshopLocation);
                while (dir != null)
                {
                    if (string.Equals(dir.Name, "workshop", StringComparison.OrdinalIgnoreCase))
                    {
                        return File.Exists(Path.Combine(dir.FullName, SteamAppworkshopAcf));
                    }
                    dir = dir.Parent;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Technique 3: Steam-copy layout heuristic.
        /// A game folder "appears to be a Steam copy" when it contains steam_api64.dll or steam_api.dll
        /// at its root, OR any ancestor path segment equals "steamapps".
        /// Case-insensitive, null-safe, no exceptions leak.
        /// </summary>
        public static bool AppearsToBeSteamCopy(string? gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                return false;

            try
            {
                // Check for Steam API DLLs at the game folder root
                var steamApi64 = Path.Combine(gameFolder, "steam_api64.dll");
                var steamApi = Path.Combine(gameFolder, "steam_api.dll");
                if (File.Exists(steamApi64) || File.Exists(steamApi))
                {
                    return true;
                }

                // Check ancestor path segments for "steamapps"
                var current = new DirectoryInfo(gameFolder);
                while (current != null)
                {
                    if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    current = current.Parent;
                }
            }
            catch
            {
                // Null-safe, no exceptions leak
            }

            return false;
        }
    }
}