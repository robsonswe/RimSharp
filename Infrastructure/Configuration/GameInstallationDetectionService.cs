using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
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
        /// </summary>
        public GameInstallationCandidate? DetectSteamInstallation()
        {
            try
            {
                var steamRoot = FindSteamRoot();
                if (steamRoot == null) return null;

                var gameFolder = FindRimWorldInSteamLibrary(steamRoot);
                if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder))
                    return null;

                var workshopFolder = FindSteamWorkshopFolder(steamRoot);
                var configFolder = RimWorldDefaultPaths.GetConfigFolder() ?? GetDefaultConfigFolder();

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
        /// </summary>
        public GameInstallationCandidate? DetectNonSteamInstallation()
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
        /// Strictly validates that a path is a RimWorld installation (rejects Factorio and other games).
        /// </summary>
        public bool IsRimWorldInstallation(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            try
            {
                // 1. Executables
                string[] executables = new[]
                {
                    "RimWorldWin64.exe", "RimWorldWin.exe", "RimWorld.exe",
                    "RimWorldLinux64", "RimWorldLinux", "RimWorld",
                    "RimWorld.app", "RimWorldMac.app"
                };

                bool hasExecutable = executables.Any(exe =>
                    File.Exists(Path.Combine(path, exe)) || Directory.Exists(Path.Combine(path, exe)));

                // 2. Official Core Mod About.xml (<packageId>ludeon.rimworld</packageId>)
                string coreAboutXml = Path.Combine(path, "Data", "Core", "About", "About.xml");
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
                    var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var linuxPaths = new[]
                    {
                        Path.Combine(userHome, ".steam", "debian-installation"),
                        Path.Combine(userHome, ".steam", "steam"),
                        Path.Combine(userHome, ".local", "share", "Steam")
                    };
                    return linuxPaths.FirstOrDefault(Directory.Exists);
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
                if (File.Exists(libraryFolders))
                {
                    foreach (var libPath in ParseLibraryFolders(libraryFolders))
                    {
                        var libGame = Path.Combine(libPath, "steamapps", "common", "RimWorld");
                        if (IsRimWorldInstallation(libGame))
                        {
                            return libGame;
                        }
                    }
                }

                return null;
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