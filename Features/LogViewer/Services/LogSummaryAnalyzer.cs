#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// The "which file am I even reading, and is it fresh" report: log identity,
    /// severity counts, the mods the log says it loaded, the memory figures it
    /// reports, and the troubleshooter's findings ranked on top.
    /// </summary>
    public sealed record LogSummaryReport(
        LogFileFormat Format,
        string GameVersion,
        string GameRevision,
        DateTime? SessionStart,
        DateTime? LastEntryTime,
        int TotalLines,
        int ErrorCount,
        int WarningCount,
        int ModsListed,
        IReadOnlyList<LogMemoryEntry> Memory,
        IReadOnlyList<LogDiagnosticIssue> Issues)
    {
        /// <summary>
        /// Where the error/warning counts came from: Waylith tags its messages,
        /// so its counts are exact; Unity-era logs carry no tags, so the viewer's
        /// own classifier decides.
        /// </summary>
        public string SeverityBasis => Format == LogFileFormat.Waylith
            ? "ERROR / WARN tags in the log"
            : "the viewer's line classifier (this log has no level tags)";
    }

    /// <summary>Builds the summary report from loaded log content.</summary>
    public static class LogSummaryAnalyzer
    {
        private const RegexOptions Options =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>Waylith severity tag at the head of a message line.</summary>
        private static readonly Regex WaylithLevel = new(
            @"^\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(?:\.\d+)?\s+(INFO|WARN|WARNING|ERROR|DEBUG|TRACE|FATAL)\s+\[",
            Options);

        private static readonly Regex Timestamp = new(
            @"^(\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(?:\.\d+)?)", Options);

        private static readonly Regex VersionLine = new(
            @"RimWorld\s+(\d+\.\d+\.\d+(?:\.\d+)?)\s+rev(\d+)", Options);

        private static readonly Regex StartedHeader = new(
            @"^\s*Started:\s+(\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(?:\.\d+)?)", Options);

        private static readonly DateTimeStyles Styles =
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;

        public static LogSummaryReport Analyze(IReadOnlyList<LogDiagnosticLine>? lines, LogFileFormat format)
        {
            var empty = new LogSummaryReport(
                format, string.Empty, string.Empty, null, null, 0, 0, 0, 0,
                Array.Empty<LogMemoryEntry>(), Array.Empty<LogDiagnosticIssue>());

            if (lines == null || lines.Count == 0) return empty;

            var version = string.Empty;
            var revision = string.Empty;
            var errors = 0;
            var warnings = 0;

            foreach (var line in lines)
            {
                var text = line.Text;
                if (string.IsNullOrEmpty(text)) continue;

                if (version.Length == 0)
                {
                    var match = VersionLine.Match(text);
                    if (match.Success)
                    {
                        version = match.Groups[1].Value;
                        revision = match.Groups[2].Value;
                    }
                }

                if (format == LogFileFormat.Waylith)
                {
                    CountWaylithLevel(text, ref errors, ref warnings);
                }
                else
                {
                    CountWithClassifier(text, ref errors, ref warnings);
                }
            }

            return new LogSummaryReport(
                format,
                version,
                revision,
                SessionStart.Invoke(lines),
                LastEntryTime(lines),
                lines.Count,
                errors,
                warnings,
                LogModListScanner.CountMods(lines),
                LogMemoryAnalyzer.Analyze(lines, format),
                LogIssueDetector.Analyze(lines));
        }

        /// <summary>Waylith tags its messages, so the level is read, not guessed.</summary>
        private static void CountWaylithLevel(string line, ref int errors, ref int warnings)
        {
            var match = WaylithLevel.Match(line);
            if (!match.Success) return;

            switch (match.Groups[1].Value.ToUpperInvariant())
            {
                case "ERROR":
                case "FATAL":
                    errors++;
                    break;
                case "WARN":
                case "WARNING":
                    warnings++;
                    break;
            }
        }

        /// <summary>
        /// Unity-era logs carry no tags, so the viewer's own classifier decides
        /// what counts as an error or a warning — the same judgement the message
        /// counts already show, keeping the two surfaces consistent.
        /// </summary>
        private static void CountWithClassifier(string line, ref int errors, ref int warnings)
        {
            var classification = LogLineClassifier.ClassifyMessage(line);
            if (classification.IsError || classification.IsException) errors++;
            if (classification.IsWarning) warnings++;
        }

        /// <summary>
        /// The session's start: the Waylith log's own banner, or the earliest
        /// message timestamp. The banner's clock is the writing machine's wall
        /// clock — exactly like the message timestamps — so its trailing UTC
        /// offset is deliberately ignored: converting it to this machine's zone
        /// would shift the whole timeline by the zone difference.
        /// </summary>
        private static readonly Func<IReadOnlyList<LogDiagnosticLine>, DateTime?> SessionStart = lines =>
        {
            var scanned = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text)) continue;
                if (scanned++ >= 20) break;

                var started = StartedHeader.Match(line.Text);
                if (started.Success &&
                    DateTime.TryParse(started.Groups[1].Value, CultureInfo.InvariantCulture, Styles, out var wallClock))
                {
                    return wallClock;
                }
            }

            return EarliestTimestamp(lines);
        };

        private static DateTime? LastEntryTime(IReadOnlyList<LogDiagnosticLine> lines)
        {
            DateTime? last = null;
            foreach (var line in lines)
            {
                var stamp = TryTimestamp(line.Text);
                if (stamp.HasValue)
                {
                    last = stamp;
                }
            }

            return last;
        }

        private static DateTime? EarliestTimestamp(IReadOnlyList<LogDiagnosticLine> lines)
        {
            foreach (var line in lines)
            {
                var stamp = TryTimestamp(line.Text);
                if (stamp.HasValue)
                {
                    return stamp;
                }
            }

            return null;
        }

        private static DateTime? TryTimestamp(string text)
        {
            var match = Timestamp.Match(text);
            if (!match.Success) return null;

            return DateTime.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed.ToLocalTime()
                : null;
        }
    }
}
