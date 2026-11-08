#nullable enable
using System;
using System.Collections.Generic;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// One log line with its 1-based position in the file, so a finding can say
    /// "first at line N" and a reader can jump there.
    /// </summary>
    public readonly record struct LogDiagnosticLine(long LineNumber, string Text);

    /// <summary>
    /// Builds the window the diagnostics analyze. Startup data lives at the head
    /// of a log and repeated failures live at the tail, but the display store
    /// keeps only the newest slice of a big log (it evicts oldest-first once the
    /// 10 MB cap is hit). So the diagnostics read the file's head directly and
    /// merge it with whatever the store still retains, keyed by line number:
    /// every line is analyzed exactly once, and line numbers stay absolute to
    /// the file instead of shifting with the cap.
    /// </summary>
    public static class LogDiagnosticsWindow
    {
        /// <summary>How much of a log's head is always read for the diagnostics.</summary>
        public const int HeadBytes = 8 * 1024 * 1024;

        /// <summary>
        /// Merges a head read with the store's retained window. The head wins
        /// where the two overlap (the store keeps the original line numbering
        /// from its first retained line, so the overlap is exact).
        /// </summary>
        public static List<LogDiagnosticLine> Build(
            string? headText,
            long storeFirstLineNumber,
            IReadOnlyList<string>? storeLines)
        {
            var window = new List<LogDiagnosticLine>();
            var seen = new HashSet<long>();
            var number = 1L;

            if (!string.IsNullOrEmpty(headText))
            {
                var parts = headText
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n');

                // A trailing newline leaves one empty element that is not a line
                // in the file — the same rule the content store applies.
                if (parts.Length > 1 && parts[^1].Length == 0)
                {
                    parts = parts[..^1];
                }

                foreach (var part in parts)
                {
                    seen.Add(number);
                    window.Add(new LogDiagnosticLine(number++, part));
                }
            }

            if (storeLines != null)
            {
                number = storeFirstLineNumber;
                foreach (var line in storeLines)
                {
                    if (seen.Contains(number))
                    {
                        number++;
                        continue;
                    }

                    window.Add(new LogDiagnosticLine(number++, line ?? string.Empty));
                }
            }

            return window;
        }

        /// <summary>
        /// Human description of what was analyzed, shown on the summary tab so a
        /// partial scan is never mistaken for a whole-file count.
        /// </summary>
        public static string DescribeScope(bool headCoversWholeFile, long storeLinesEvicted)
        {
            // Nothing evicted from the store means the head+store union already
            // holds every line, even when the head window was partial.
            if (headCoversWholeFile || storeLinesEvicted == 0)
            {
                return "Whole log analyzed.";
            }

            var tail = storeLinesEvicted > 0
                ? "the newest lines still retained (older ones exceeded the display memory cap)"
                : "all lines the viewer holds";
            return $"Analyzed the first {FormatBudget(HeadBytes)} of the log plus {tail}.";
        }

        private static string FormatBudget(int bytes)
        {
            double size = bytes;
            foreach (var unit in new[] { "B", "KB", "MB", "GB" })
            {
                if (size < 1024) return FormattableString.Invariant($"{size:F0} {unit}");
                size /= 1024;
            }

            return FormattableString.Invariant($"{size:F0} TB");
        }
    }
}
