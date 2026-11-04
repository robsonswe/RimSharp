using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Configuration
{
    /// <summary>
    /// Service for detecting Steam installations and configuring game instances.
    /// Based on RimSort's path_autodetect_service.py.
    /// </summary>
    public class SteamDetectionService
    {
        private const string RimWorldAppId = "294100";
        private const string SteamAppworkshopAcf = "appworkshop_294100.acf";

        /// <summary>
        /// Detect Steam installation and configure game instance.
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

            // Set config folder based on platform
            instance.ConfigFolder = GetConfigFolder();

            // Set local folder (typically game_folder/Mods)
            instance.LocalFolder = Path.Combine(instance.GameFolder, "Mods");

            // Validate and fix any inconsistencies
            instance.ValidateAndFix();

            return instance.IsSteamInstallation;
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
        /// Based on RimSort's win_find_steam.py.
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
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "debian-installation"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "snap", "steam", "common", ".local", "share", "Steam")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    // Verify it's a valid Steam installation
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
                    // Verify it's a valid Steam installation
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

            // Check all libraries for RimWorld
            foreach (var library in libraryFolders)
            {
                var rimWorldPath = Path.Combine(library, "steamapps", "common", "RimWorld");
                if (IsValidRimWorldFolder(rimWorldPath))
                {
                    return rimWorldPath;
                }

                // Also check for RimWorld in the root of the library
                var rimWorldInRoot = Path.Combine(library, "RimWorld");
                if (IsValidRimWorldFolder(rimWorldInRoot))
                {
                    return rimWorldInRoot;
                }
            }

            // Fallback: check steamRoot common folder
            var defaultCommon = Path.Combine(steamRoot, "steamapps", "common", "RimWorld");
            if (IsValidRimWorldFolder(defaultCommon))
            {
                return defaultCommon;
            }

            return null;
        }

        /// <summary>
        /// Parse Steam libraryfolders.vdf to get all library paths.
        /// Checks both steamapps/libraryfolders.vdf (modern) and config/libraryfolders.vdf (legacy).
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
                // Continue with whatever libraries we found
            }

            return libraries.ToList();
        }

        /// <summary>
        /// Get workshop folder path from game folder and Steam root.
        /// Strips path up to steamapps folder to build steamapps/workshop/content/294100.
        /// </summary>
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

            // Fallback to the main Steam workshop folder
            return Path.Combine(steamRoot, "steamapps", "workshop", "content", RimWorldAppId);
        }

        /// <summary>
        /// Get config folder path based on platform.
        /// </summary>
        private string GetConfigFolder()
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

            // Fallback
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimWorld", "Config");
        }

        /// <summary>
        /// Validate that a directory looks like a RimWorld installation.
        /// </summary>
        public bool IsValidRimWorldFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                return false;
            }

            // Check for Version.txt
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
                    // Continue to other checks
                }
            }

            // Check for Data folder
            var dataFolder = Path.Combine(folderPath, "Data");
            if (Directory.Exists(dataFolder))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a path points to a RimWorld Steam Workshop content folder.
        /// </summary>
        public bool IsRimWorldWorkshopFolder(string path)
        {
            return Path.GetFileName(path) == RimWorldAppId;
        }

        /// <summary>
        /// Validate that the appworkshop_294100.acf file exists for the Steam Workshop location.
        /// </summary>
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
    }
}