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
            return new string?[]
            {
                // Unity persistent data path on Linux.
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "unity3d", CompanyFolder, ProductFolder, ConfigSubFolder),
                // Unity persistent data path on Windows (LocalLow).
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", CompanyFolder, ProductFolder, ConfigSubFolder),
                // Unity persistent data path on macOS.
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RimWorld", ConfigSubFolder),
            }
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
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
    }
}
