#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>Resolved Player.log source paths for one game instance.</summary>
    /// <param name="Current">The active session's log; null when not present.</param>
    /// <param name="Previous">The previous session's log; null when not present.</param>
    public sealed record LogSourcePaths(string? Current, string? Previous);

    /// <summary>
    /// Locates RimWorld's Player.log. On Windows/Linux the log lives beside the
    /// config folder (Unity writes both under the same LocalLow folder). On macOS
    /// Unity writes logs under ~/Library/Logs/Ludeon Studios/RimWorld by Ludeon Studios.
    /// The relative layout is version-gated: Waylith builds (1.7+, the .NET-native
    /// engine) write logs into a <c>Logs/</c> subfolder, while Unity-era builds
    /// (1.6 and earlier) write <c>Player.log</c> at the folder root. Resolution is
    /// pure path work over injected inputs so it is testable without a game install.
    /// </summary>
    public static class PlayerLogLocator
    {
        public const string CurrentFileName = "Player.log";
        public const string PreviousFileName = "Player-prev.log";
        public const string LogsFolderName = "Logs";

        // macOS Unity log folder product names (match RimWorldDefaultPaths constants)
        private const string MacCompanyFolder = "Ludeon Studios";
        private const string MacProductFolder = "RimWorld by Ludeon Studios";

        /// <summary>First Waylith game version (major.minor) using the Logs/ subfolder.</summary>
        public static readonly Version WaylithLogsLayoutVersion = new(1, 7);

        /// <summary>
        /// Test seam: override the current platform for testing macOS resolution on non-macOS.
        /// When null (default), uses RuntimeInformation.IsOSPlatform.
        /// </summary>
        internal static OSPlatform? TestPlatformOverride { get; set; }

        private static bool IsMacOS() =>
            TestPlatformOverride.HasValue
                ? TestPlatformOverride.Value == OSPlatform.OSX
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>
        /// Resolves both log sources for the given config folder under the
        /// layout implied by <paramref name="majorGameVersion"/> (e.g. "1.7").
        /// Paths are only returned when the file actually exists, so callers can
        /// treat non-null as "readable now".
        /// </summary>
        public static LogSourcePaths Resolve(string? configFolder, string? majorGameVersion)
        {
            // macOS: Unity logs are under ~/Library/Logs, not beside the config folder
            if (IsMacOS())
            {
                return ResolveMacOS(majorGameVersion, GetHomeFolder);
            }

            var gameFolder = GetGameFolder(configFolder);
            if (gameFolder == null)
            {
                return new LogSourcePaths(null, null);
            }

            var logFolder = UsesLogsSubfolder(majorGameVersion)
                ? Path.Combine(gameFolder, LogsFolderName)
                : gameFolder;

            var current = Path.Combine(logFolder, CurrentFileName);
            var previous = Path.Combine(logFolder, PreviousFileName);
            return new LogSourcePaths(
                File.Exists(current) ? current : null,
                File.Exists(previous) ? previous : null);
        }

        /// <summary>
        /// macOS-specific resolution. Uses a seam for the home folder so tests
        /// can inject a temp root without touching the real HOME.
        /// </summary>
        internal static LogSourcePaths ResolveMacOS(string? majorGameVersion, Func<string> getHomeFolder)
        {
            var home = getHomeFolder();
            if (string.IsNullOrEmpty(home))
            {
                return new LogSourcePaths(null, null);
            }

            var logRoot = Path.Combine(home, "Library", "Logs", MacCompanyFolder, MacProductFolder);
            var logFolder = UsesLogsSubfolder(majorGameVersion)
                ? Path.Combine(logRoot, LogsFolderName)
                : logRoot;

            var current = Path.Combine(logFolder, CurrentFileName);
            var previous = Path.Combine(logFolder, PreviousFileName);
            return new LogSourcePaths(
                File.Exists(current) ? current : null,
                File.Exists(previous) ? previous : null);
        }

        /// <summary>
        /// Seam for the user's home folder. Tests replace this to avoid touching
        /// the real HOME. Defaults to Environment.GetFolderPath(UserProfile).
        /// </summary>
        internal static Func<string> GetHomeFolder { get; set; } = () =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// True when the game version writes logs into the Logs/ subfolder
        /// (Waylith, 1.7+). An unknown or unparseable version falls back to the
        /// classic root layout.
        /// </summary>
        public static bool UsesLogsSubfolder(string? majorGameVersion) =>
            Version.TryParse(majorGameVersion, out var version) &&
            version >= WaylithLogsLayoutVersion;

        /// <summary>
        /// The folder holding Player.log: the parent of the config folder
        /// (…/RimWorld by Ludeon Studios/Config → …/RimWorld by Ludeon Studios).
        /// Trailing separators are tolerated; root or blank inputs yield null.
        /// </summary>
        public static string? GetGameFolder(string? configFolder)
        {
            if (string.IsNullOrWhiteSpace(configFolder)) return null;

            var trimmed = configFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0) return null;

            var parent = Path.GetDirectoryName(trimmed);
            return string.IsNullOrEmpty(parent) ? null : parent;
        }
    }
}
