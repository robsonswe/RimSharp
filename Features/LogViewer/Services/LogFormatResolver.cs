#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// Which engine generation produced a Player.log. Waylith (1.7+, the
    /// .NET-native engine) timestamps and tags every message and has no Unity
    /// profiler output; Unity-era builds (below 1.7) have neither timestamps
    /// nor tags but carry profiler and allocator sections.
    /// </summary>
    public enum LogFileFormat
    {
        Unknown,
        Waylith,
        Unity
    }

    /// <summary>
    /// Picks the engine generation for a log. The installed game version is the
    /// authoritative signal (1.7 and above run Waylith), but a hand-opened file
    /// may come from a different generation than the installed game, so the
    /// log's own content decides first and the version only settles logs whose
    /// content is inconclusive.
    /// </summary>
    public static class LogFormatResolver
    {
        /// <summary>Header line present at the top of every Waylith log.</summary>
        private static readonly Regex WaylithHeader = new(@"^\s*===\s*Waylith log\s*===", RegexOptions.Compiled);

        /// <summary>Waylith message grammar: timestamp, level tag, bracketed category.</summary>
        private static readonly Regex WaylithMessage = new(
            @"^\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(\.\d+)?\s+(INFO|WARN|WARNING|ERROR|DEBUG|TRACE|FATAL)\s+\[[^\]]+\]",
            RegexOptions.Compiled);

        /// <summary>Lines only Unity-era RimWorld logs start with.</summary>
        private static readonly Regex UnityMessage = new(
            @"(^Mono path|^Initialize engine version|^GfxDevice:|^\s*Begin MonoManager ReloadAssembly|^UnloadTime:)",
            RegexOptions.Compiled);

        /// <summary>
        /// The engine generation implied by a game version string such as "1.7"
        /// (major.minor). Versions at or above the Waylith threshold run
        /// Waylith; below it, Unity. Missing or unparseable versions yield
        /// <see cref="LogFileFormat.Unknown"/>.
        /// </summary>
        public static LogFileFormat FromGameVersion(string? majorGameVersion)
        {
            if (!Version.TryParse(majorGameVersion, out var version))
            {
                return LogFileFormat.Unknown;
            }

            return version >= PlayerLogLocator.WaylithLogsLayoutVersion
                ? LogFileFormat.Waylith
                : LogFileFormat.Unity;
        }

        /// <summary>Reads the engine generation straight off the log content.</summary>
        public static LogFileFormat FromContent(IReadOnlyList<string>? lines)
        {
            if (lines == null) return LogFileFormat.Unknown;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (WaylithHeader.IsMatch(line)) return LogFileFormat.Waylith;
                if (WaylithMessage.IsMatch(line)) return LogFileFormat.Waylith;
                if (UnityMessage.IsMatch(line)) return LogFileFormat.Unity;
            }

            return LogFileFormat.Unknown;
        }

        /// <summary>
        /// Content decides when it can; the installed game version settles the
        /// rest (a hand-opened log with no recognizable grammar).
        /// </summary>
        public static LogFileFormat Resolve(IReadOnlyList<string>? lines, string? majorGameVersion)
        {
            var fromContent = FromContent(lines);
            return fromContent != LogFileFormat.Unknown ? fromContent : FromGameVersion(majorGameVersion);
        }

        /// <summary>Human label for the summary and startup tabs.</summary>
        public static string Label(LogFileFormat format) => format switch
        {
            LogFileFormat.Waylith => "Waylith (1.7+)",
            LogFileFormat.Unity => "Unity (pre-1.7)",
            _ => "Unrecognized"
        };
    }
}
