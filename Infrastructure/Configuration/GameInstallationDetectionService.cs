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
                var configFolder = ResolveConfigFolderForSteamRoot(steamRoot);

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

                var configFolder = RimWorldDefaultPaths.GetConfigFolder() ?? GetDefaultConfigFolder();

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
        /// Structurally validates a user's manual "this is a Steam RimWorld" claim for
        /// the given game directory (trust-ladder rung 2). Falls well short
        /// of proving provenance — it verifies configuration consistency:
        ///   1. the directory exists and looks like RimWorld (IsRimWorldInstallation),
        ///   2. it sits directly under &lt;library&gt;\steamapps\common\ (Steam's own fixed
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

        private static string GetDefaultConfigFolder()
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
        private static string ResolveConfigFolderForSteamRoot(string? steamRoot)
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

            return null;
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
        /// </summary>
        public bool IsRimWorldInstallation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            try
            {
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
                    var macPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam");
                    if (Directory.Exists(macPath)) return macPath;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string? FindRimWorldInSteamLibrary(string steamRoot)
        {
            try
            {
                var mainSteamApps = Path.Combine(steamRoot, "steamapps");
                if (!Directory.Exists(mainSteamApps)) return null;

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
        /// Reads &lt;steamapps&gt;\appmanifest_294100.acf, resolves the recorded "installdir"
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
    }
}