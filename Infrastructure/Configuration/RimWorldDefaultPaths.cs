#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimSharp.Infrastructure.Configuration
{
    /// <summary>
    /// Provides platform-aware default locations for RimWorld folders, used as
    /// starting points when the user browses for the game or config folder.
    /// </summary>
    public static class RimWorldDefaultPaths
    {
        private const string CompanyFolder = "Ludeon Studios";
        private const string ProductFolder = "RimWorld by Ludeon Studios";
        private const string ConfigSubFolder = "Config";
        private const string SteamAppId = "294100";

        /// <summary>
        /// Returns the RimWorld config folder that contains ModsConfig.xml for
        /// the current platform, or null when the folder does not exist.
        /// <paramref name="exists"/> allows tests to probe candidates without
        /// touching the real filesystem.
        /// </summary>
        public static string? GetConfigFolder(Func<string, bool>? exists = null)
        {
            return ResolveFirstExisting(GetConfigFolderCandidates(), exists);
        }

        /// <summary>
        /// Returns a common RimWorld game install location for the current
        /// platform (Steam library first), or null when none exists.
        /// <paramref name="exists"/> allows tests to probe candidates without
        /// touching the real filesystem.
        /// </summary>
        public static string? GetGameFolderHint(Func<string, bool>? exists = null)
        {
            return ResolveFirstExisting(GetGameFolderHintCandidates(), exists);
        }

        internal static string[] GetConfigFolderCandidates()
        {
            var linuxPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "unity3d", CompanyFolder, ProductFolder, ConfigSubFolder);
            var windowsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", CompanyFolder, ProductFolder, ConfigSubFolder);
            var macOsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RimWorld", ConfigSubFolder);

            string[] candidates = OperatingSystem.IsWindows()
                ? new[] { windowsPath, linuxPath, macOsPath }
                : OperatingSystem.IsLinux()
                    ? new[] { linuxPath, windowsPath, macOsPath }
                    : OperatingSystem.IsMacOS()
                        ? new[] { macOsPath, linuxPath, windowsPath }
                        : new[] { linuxPath, windowsPath, macOsPath };

            return candidates
                .Where(path => !string.IsNullOrEmpty(path))
                .ToArray();
        }

        internal static string[] GetGameFolderHintCandidates()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new string?[]
            {
                // Steam library on Linux (legacy and default locations).
                Path.Combine(userProfile, ".steam", "steam", "steamapps", "common", "RimWorld"),
                Path.Combine(userProfile, ".local", "share", "Steam", "steamapps", "common", "RimWorld"),
                // Steam library on Windows.
                Path.Combine("C:", "Program Files (x86)", "Steam", "steamapps", "common", "RimWorld"),
                // Steam library on macOS.
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Steam", "steamapps", "common", "RimWorld"),
            }
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToArray();
        }

        private static string? ResolveFirstExisting(IEnumerable<string> candidates, Func<string, bool>? exists)
        {
            var directoryExists = exists ?? (path => Directory.Exists(path));

            foreach (var candidate in candidates)
            {
                if (directoryExists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Steam root candidates on Linux, in probe order (RimSort reference behavior):
        /// native/legacy installs first, then sandboxed (Flatpak, Snap) installs.
        /// Callers apply their own validity checks (steamapps dir / libraryfolders.vdf)
        /// on top of these paths.
        /// </summary>
        internal static string[] GetLinuxSteamRootCandidates(string userProfile)
        {
            return new[]
            {
                Path.Combine(userProfile, ".steam", "debian-installation"),
                Path.Combine(userProfile, ".steam", "steam"),
                Path.Combine(userProfile, ".local", "share", "Steam"),
                Path.Combine(userProfile, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                Path.Combine(userProfile, "snap", "steam", "common", ".local", "share", "Steam")
            };
        }

        /// <summary>
        /// Proton prefix config path for Steam Play users. When RimWorld runs through
        /// Proton on Linux, Unity writes its config inside the game's compat prefix
        /// instead of the native <c>~/.config/unity3d/...</c> path — so detection must
        /// prefer the prefix when it exists (RimSort reference behavior).
        /// Exposed separately from <see cref="GetProtonConfigFolder"/> so tests can pin
        /// the exact path shape without a filesystem.
        /// </summary>
        public static string BuildProtonConfigPath(string steamRoot)
        {
            return Path.Combine(
                steamRoot,
                "steamapps", "compatdata", SteamAppId,
                "pfx", "drive_c", "users", "steamuser",
                "AppData", "LocalLow", CompanyFolder, ProductFolder, ConfigSubFolder);
        }

        /// <summary>
        /// Returns the Proton prefix config folder under the given Steam root when it
        /// exists on disk, or null (caller falls back to the native Unity config path).
        /// <paramref name="exists"/> allows tests to probe candidates without touching
        /// the real filesystem. Valid on any OS — callers gate on Linux themselves.
        /// </summary>
        public static string? GetProtonConfigFolder(string? steamRoot, Func<string, bool>? exists = null)
        {
            if (string.IsNullOrWhiteSpace(steamRoot))
            {
                return null;
            }

            var protonConfig = BuildProtonConfigPath(steamRoot);
            return (exists ?? Directory.Exists)(protonConfig) ? protonConfig : null;
        }
    }
}
