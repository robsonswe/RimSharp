#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// One finding from the troubleshooter scan: the matched signature, how many
    /// lines matched it, and where the first match sits in the log file.
    /// </summary>
    public sealed record LogDiagnosticIssue(
        LogDiagnosticSeverity Severity,
        string Title,
        string Description,
        string Suggestion,
        int Count,
        long FirstLineNumber)
    {
        /// <summary>Severity as shown in the troubleshooter list.</summary>
        public string SeverityLabel => LogDiagnosticPatterns.SeverityLabel(Severity);

        /// <summary>Theme brush key the view resolves for this severity.</summary>
        public string SeverityBrushKey => Severity switch
        {
            LogDiagnosticSeverity.Error => "BrushTextError",
            LogDiagnosticSeverity.Warning => "BrushTextWarning",
            _ => "BrushTextMuted"
        };
    }

    /// <summary>
    /// Scans loaded log lines against the known-issue catalog. Findings are
    /// deduplicated by title with an occurrence count, ordered errors first then
    /// warnings then info, most frequent first inside each severity. A line can
    /// raise several findings (a null reference inside a UI-loop exception is
    /// both), so the counts are per signature, not per line.
    /// </summary>
    public static class LogIssueDetector
    {
        public static List<LogDiagnosticIssue> Analyze(IReadOnlyList<LogDiagnosticLine>? lines)
        {
            var found = new Dictionary<string, LogDiagnosticIssue>(StringComparer.Ordinal);
            if (lines == null) return new List<LogDiagnosticIssue>();

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line.Text)) continue;

                foreach (var pattern in LogDiagnosticPatterns.All)
                {
                    if (!pattern.Pattern.IsMatch(line.Text)) continue;

                    if (found.TryGetValue(pattern.Title, out var existing))
                    {
                        found[pattern.Title] = existing with { Count = existing.Count + 1 };
                        continue;
                    }

                    found[pattern.Title] = new LogDiagnosticIssue(
                        pattern.Severity,
                        pattern.Title,
                        pattern.Description,
                        pattern.Suggestion,
                        1,
                        line.LineNumber);
                }
            }

            return found.Values
                .OrderBy(issue => issue.Severity)
                .ThenByDescending(issue => issue.Count)
                .ThenBy(issue => issue.Title, StringComparer.Ordinal)
                .ToList();
        }
    }
}
