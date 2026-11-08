#nullable enable
using System;
using System.Linq;
using RimSharp.Features.AppLogs.Services;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>Whether a file handed to the viewer is a log it can read.</summary>
    public enum LogFileVerdict
    {
        /// <summary>A recognized log: a Player.log of either engine generation, a shared upload, an app log.</summary>
        Valid,

        /// <summary>Not a log this viewer reads. Nothing is loaded; the file is explained instead.</summary>
        Invalid
    }

    /// <summary>The verdict plus the sentence that explains it to the user.</summary>
    /// <param name="FormatRecognized">
    /// True when the file's *content* has a shape the viewer analyzes — either
    /// engine generation, a shared upload, or an app log. A file that passes on
    /// its name alone is not format-recognized: the name says nothing about
    /// what is inside, so the auto-load path keeps its reports locked for those.
    /// </param>
    public sealed record LogFileValidation(LogFileVerdict Verdict, string Message, bool FormatRecognized);

    /// <summary>
    /// Judges whether a file is a log this viewer reads, from a bounded read
    /// of its head. This viewer reads two families only: a RimWorld
    /// Player.log of either engine generation, and a shared HugsLib-style
    /// upload. The game's own file names load on name alone (a truncated
    /// write still holds the tail of a session). Everything else is Invalid —
    /// RimSharp's own app logs included, which belong to the App Logs window:
    /// the file is not loaded, its content area says why, and the report tabs
    /// stay locked rather than reporting on content that was never read.
    /// </summary>
    public static class LogFileValidator
    {
        /// <summary>How much of a file the validator reads to reach a verdict.</summary>
        public const int ProbeBytes = 64 * 1024;

        /// <summary>File names recognized as logs regardless of content.</summary>
        private static readonly string[] KnownFileNames =
        {
            "Player.log",
            "Player-prev.log",
            "output_log.txt"
        };

        public static LogFileValidation Classify(string? headText, string? fileName)
        {
            if (string.IsNullOrEmpty(headText))
            {
                return new LogFileValidation(LogFileVerdict.Invalid, "This file is empty, so nothing was loaded.", FormatRecognized: false);
            }

            if (LooksBinary(headText!))
            {
                return new LogFileValidation(
                    LogFileVerdict.Invalid,
                    "This file is not a text log, so nothing was loaded.",
                    FormatRecognized: false);
            }

            if (HugsLogEnvelopeParser.LooksLikeEnvelope(headText))
            {
                return new LogFileValidation(
                    LogFileVerdict.Valid,
                    "Shared log upload — the publisher's metadata is summarized on the Summary tab.",
                    FormatRecognized: true);
            }

            var format = LogFormatResolver.FromContent(
                headText!.Split('\n').Select(l => l.TrimEnd('\r')).ToList());
            if (format == LogFileFormat.Waylith || format == LogFileFormat.Unity)
            {
                return new LogFileValidation(
                    LogFileVerdict.Valid,
                    $"RimWorld Player.log ({LogFormatResolver.Label(format)}).",
                    FormatRecognized: true);
            }

            // This viewer does not read the app's own logs — the App Logs
            // window does — so they are invalid here, with a message that
            // says where they belong.
            var name = fileName ?? string.Empty;
            if (AppLogReader.LooksLikeContent(headText!) ||
                name.StartsWith("RimSharp_", StringComparison.OrdinalIgnoreCase))
            {
                return new LogFileValidation(
                    LogFileVerdict.Invalid,
                    "This is a RimSharp app log — open it in the App Logs window (Menu → App Logs).",
                    FormatRecognized: true);
            }

            if (KnownFileNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                return new LogFileValidation(
                    LogFileVerdict.Valid,
                    "Named like the game's own log, so it opens as one.",
                    FormatRecognized: false);
            }

            return new LogFileValidation(
                LogFileVerdict.Invalid,
                "This file doesn't look like a RimWorld Player.log or a shared log upload, so nothing was loaded.",
                FormatRecognized: false);
        }

        /// <summary>
        /// True for content that is not readable text: embedded NUL bytes, or a
        /// share of replacement characters high enough to mean the decode failed
        /// (a UTF-16 file renamed to .txt lands here).
        /// </summary>
        public static bool LooksBinary(string text)
        {
            if (text.IndexOf('\0') >= 0)
            {
                return true;
            }

            var replacements = text.Count(c => c == '\uFFFD');
            return (double)replacements / text.Length > 0.02;
        }
    }
}
