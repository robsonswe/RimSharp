#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>Filter modes offered by the viewer; mirrors the reference's combo box.</summary>
    public enum LogFilterMode
    {
        AllEntries,
        InfosOnly,
        KeybindsOnly,
        ModIssues,
        WarningsOnly,
        ErrorsOnly,
        ExceptionsOnly,
        AllIssues
    }

    /// <summary>
    /// Category membership for one line. A line can belong to several categories
    /// (statistics count membership, not exclusivity); <see cref="Severity"/> is
    /// the single display color chosen by severity priority.
    /// </summary>
    public readonly record struct LogLineClassification(
        LogLineSeverity Severity,
        bool IsInfo,
        bool IsKeybind,
        bool IsModIssue,
        bool IsWarning,
        bool IsError,
        bool IsException,
        bool IsStackTrace);

    /// <summary>Running category counters for the loaded content.</summary>
    public sealed class LogStats
    {
        public int TotalLines { get; set; }
        public int Infos { get; set; }
        public int Keybinds { get; set; }
        public int ModIssues { get; set; }
        public int Warnings { get; set; }
        public int Errors { get; set; }
        public int Exceptions { get; set; }

        /// <summary>
        /// Lines matching at least one issue category. Counted per line (a line
        /// that is both an error and a mod issue counts once), not as a sum of
        /// the category buckets.
        /// </summary>
        public int AllIssues { get; private set; }

        public void Add(LogLineClassification classification)
        {
            TotalLines++;
            if (classification.IsInfo) Infos++;
            if (classification.IsKeybind) Keybinds++;
            if (classification.IsModIssue) ModIssues++;
            if (classification.IsWarning) Warnings++;
            if (classification.IsError) Errors++;
            if (classification.IsException) Exceptions++;
            if (classification.IsKeybind || classification.IsModIssue ||
                classification.IsWarning || classification.IsError ||
                classification.IsException)
            {
                AllIssues++;
            }
        }
    }

    /// <summary>
    /// Pure line-level analysis: classification, filter matching, statistics and
    /// repeated-line collapse. No IO, no state — everything here is unit-testable
    /// over synthetic lines.
    /// </summary>
    public static class LogLineClassifier
    {
        /// <summary>
        /// Classifies one line. Stack-trace frames inherit exception severity so
        /// trace bodies stay visually attached to their exception line.
        /// </summary>
        public static LogLineClassification Classify(string? line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return new LogLineClassification(LogLineSeverity.None, false, false, false, false, false, false, false);
            }

            bool isException = LogPatterns.Exception.IsMatch(line);
            bool isStackTrace = LogPatterns.StackTrace.IsMatch(line);
            bool isError = LogPatterns.Error.IsMatch(line);
            bool isWarning = LogPatterns.Warning.IsMatch(line);
            bool isModIssue = LogPatterns.ModIssue.IsMatch(line);
            bool isKeybind = LogPatterns.Keybind.IsMatch(line);
            bool isInfo = LogPatterns.Info.IsMatch(line);

            LogLineSeverity severity;
            if (isException || isStackTrace) severity = LogLineSeverity.Exception;
            else if (isError) severity = LogLineSeverity.Error;
            else if (isWarning) severity = LogLineSeverity.Warning;
            else if (isModIssue) severity = LogLineSeverity.ModIssue;
            else if (isKeybind) severity = LogLineSeverity.Keybind;
            else if (isInfo) severity = LogLineSeverity.Info;
            else severity = LogLineSeverity.None;

            return new LogLineClassification(
                severity, isInfo, isKeybind, isModIssue, isWarning, isError, isException, isStackTrace);
        }

        /// <summary>True when the line's categories satisfy the given filter mode.</summary>
        public static bool MatchesFilter(LogLineClassification classification, LogFilterMode mode) =>
            mode switch
            {
                LogFilterMode.AllEntries => true,
                LogFilterMode.InfosOnly => classification.IsInfo,
                LogFilterMode.KeybindsOnly => classification.IsKeybind,
                LogFilterMode.ModIssues => classification.IsModIssue,
                LogFilterMode.WarningsOnly => classification.IsWarning,
                LogFilterMode.ErrorsOnly => classification.IsError,
                LogFilterMode.ExceptionsOnly => classification.IsException,
                LogFilterMode.AllIssues => classification.IsKeybind || classification.IsModIssue ||
                                            classification.IsWarning || classification.IsError ||
                                            classification.IsException,
                _ => true
            };

        /// <summary>
        /// Mod-name substring filter; composes with the mode filter. Empty/whitespace
        /// matches everything.
        /// </summary>
        public static bool MatchesModFilter(string? line, string? modFilter)
        {
            if (string.IsNullOrWhiteSpace(modFilter)) return true;
            return line != null && line.Contains(modFilter.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Accumulates category counters over the given lines.</summary>
        public static LogStats Analyze(IEnumerable<string?> lines)
        {
            var stats = new LogStats();
            foreach (var line in lines)
            {
                stats.Add(Classify(line));
            }
            return stats;
        }

        /// <summary>
        /// Collapses consecutive identical lines into one line annotated with the
        /// repeat count, matching the reference's display behavior.
        /// </summary>
        public static List<string> CollapseRepeated(IEnumerable<string?> lines)
        {
            var result = new List<string>();
            string? previous = null;
            var count = 0;

            foreach (var line in lines)
            {
                if (line == null) continue;
                if (previous != null && line == previous)
                {
                    count++;
                    continue;
                }

                if (previous != null)
                {
                    result.Add(count > 1 ? $"{previous} (Repeated {count} times)" : previous);
                }

                previous = line;
                count = 1;
            }

            if (previous != null)
            {
                result.Add(count > 1 ? $"{previous} (Repeated {count} times)" : previous);
            }

            return result;
        }
    }
}
