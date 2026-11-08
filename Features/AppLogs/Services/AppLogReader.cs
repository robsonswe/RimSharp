#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RimSharp.Features.AppLogs.Services
{
    /// <summary>
    /// Reads the app's own log files. Every written line carries its
    /// timestamp, severity tag and module — the app owns the format, so the
    /// level is read, never guessed — and exception text arrives as bare
    /// continuation lines that belong to the message above them.
    /// </summary>
    public static class AppLogReader
    {
        private static readonly Regex Header = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?) \[(?<level>[A-Za-z]+)\] \[(?<module>[^\]]+)\] (?<text>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Parses log text into messages. A line without a recognized header
        /// continues the message above it; a file that never opens with a
        /// header keeps its leading lines as one untagged message rather than
        /// dropping content.
        /// </summary>
        public static List<AppLogEntry> Parse(string? text)
        {
            var entries = new List<AppLogEntry>();
            if (string.IsNullOrEmpty(text))
            {
                return entries;
            }

            var lines = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            // A trailing terminator is one empty element that is not a line
            // in the file — the same rule the content store applies.
            if (lines.Length > 1 && lines[^1].Length == 0)
            {
                lines = lines[..^1];
            }

            DateTime? timestamp = null;
            var level = AppLogLevel.Info;
            var module = "General";
            var body = new List<string>();

            void Flush()
            {
                if (body.Count == 0) return;

                entries.Add(new AppLogEntry(
                    timestamp ?? DateTime.MinValue,
                    level,
                    module,
                    string.Join("\n", body))
                {
                    LineCount = body.Count
                });

                body.Clear();
                timestamp = null;
            }

            foreach (var line in lines)
            {
                var match = Header.Match(line);
                if (match.Success)
                {
                    Flush();

                    timestamp = DateTime.TryParse(
                        match.Groups["ts"].Value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal,
                        out var parsed)
                        ? parsed
                        : (DateTime?)null;
                    level = ParseLevel(match.Groups["level"].Value);
                    module = match.Groups["module"].Value;
                    body.Add(match.Groups["text"].Value);
                    continue;
                }

                // Continuation line: stack traces and multi-line exception
                // messages land here with no tag of their own.
                if (body.Count > 0)
                {
                    body.Add(line);
                    continue;
                }

                // Leading orphan (a truncated first line): keep it visible.
                body.Add(line);
            }

            Flush();
            return entries;
        }

        /// <summary>
        /// Reads a log file as messages. Encoding, sharing and chunking follow
        /// the same reader the game-log viewer uses, so a file the app is
        /// still appending to reads without locking it.
        /// </summary>
        public static List<AppLogEntry> ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new List<AppLogEntry>();
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return Parse(reader.ReadToEnd());
            }
            catch (IOException)
            {
                return new List<AppLogEntry>();
            }
            catch (UnauthorizedAccessException)
            {
                return new List<AppLogEntry>();
            }
        }

        /// <summary>
        /// True when the text reads like an app log the writer produced: tagged
        /// lines with a module. Used by the Player.log viewer's open path to
        /// recognize our own logs, since opening one there is legitimate.
        /// Two tagged lines are required — prose occasionally brackets a word,
        /// but not a timestamp plus level plus module on one line.
        /// </summary>
        public static bool LooksLikeContent(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var tagged = 0;
            for (var i = 0; i < lines.Length && i < 200; i++)
            {
                if (Header.IsMatch(lines[i])) tagged++;
                if (tagged >= 2) return true;
            }

            return false;
        }

        /// <summary>
        /// The severity tag as the writer emitted it. Unknown tags read as
        /// Info rather than hiding the line.
        /// </summary>
        public static AppLogLevel ParseLevel(string tag)
        {
            return tag.ToUpperInvariant() switch
            {
                "DEBUG" or "TRACE" or "VERBOSE" => AppLogLevel.Debug,
                "INFO" or "INFORMATION" => AppLogLevel.Info,
                "WARN" or "WARNING" => AppLogLevel.Warning,
                "ERROR" => AppLogLevel.Error,
                "CRITICAL" or "FATAL" => AppLogLevel.Critical,
                _ => AppLogLevel.Info
            };
        }

        /// <summary>Counts per severity over parsed messages, plus the total.</summary>
        public static AppLogStats CountStats(IReadOnlyList<AppLogEntry> entries)
        {
            var debug = 0;
            var info = 0;
            var warning = 0;
            var error = 0;
            var critical = 0;

            foreach (var entry in entries)
            {
                switch (entry.Level)
                {
                    case AppLogLevel.Debug: debug++; break;
                    case AppLogLevel.Info: info++; break;
                    case AppLogLevel.Warning: warning++; break;
                    case AppLogLevel.Error: error++; break;
                    case AppLogLevel.Critical: critical++; break;
                }
            }

            return new AppLogStats(debug, info, warning, error, critical, entries.Count);
        }
    }

    /// <summary>Per-severity message counts and the total.</summary>
    public sealed record AppLogStats(int Debug, int Info, int Warning, int Error, int Critical, int Total);
}
