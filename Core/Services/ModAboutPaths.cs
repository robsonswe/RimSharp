#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimSharp.Core.Services
{
    /// <summary>
    /// Resolves a mod's About folder/file. The canonical name is "About", but on
    /// case-sensitive Linux filesystems some mods ship lowercase "about" (GitHub
    /// zip quirks). The game loads those through its own case-tolerant lookups;
    /// RimSharp mirrors that: exact name first, then any directory with the same
    /// name ignoring case. On case-insensitive filesystems (Windows/macOS) the
    /// exact path already resolves any variant, so the fallback is inert there.
    /// </summary>
    public static class ModAboutPaths
    {
        /// <summary>
        /// Returns the mod's About folder. When neither an exact "About" nor a
        /// case-variant exists, the canonical path is returned (callers that only
        /// read can then check File.Exists on the derived About.xml path).
        /// Injection seams keep the fallback testable on any host OS.
        /// </summary>
        public static string GetAboutFolder(
            string modDirectory,
            Func<string, bool>? directoryExists = null,
            Func<string, IEnumerable<string>>? getDirectories = null)
        {
            var canonical = Path.Combine(modDirectory, "About");
            if ((directoryExists ?? Directory.Exists)(canonical))
            {
                return canonical;
            }

            var enumerate = getDirectories ?? (dir =>
                Directory.Exists(dir) ? Directory.EnumerateDirectories(dir) : Enumerable.Empty<string>());

            var match = enumerate(modDirectory)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "About", StringComparison.OrdinalIgnoreCase));

            return match ?? canonical;
        }

        /// <summary>Returns the mod's About.xml path using the resolved About folder.</summary>
        public static string GetAboutFilePath(
            string modDirectory,
            Func<string, bool>? directoryExists = null,
            Func<string, IEnumerable<string>>? getDirectories = null)
        {
            return Path.Combine(GetAboutFolder(modDirectory, directoryExists, getDirectories), "About.xml");
        }
    }
}
