#nullable enable
using System.Text.RegularExpressions;

namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// The single regex set shared by line classification, statistics, filtering
    /// and highlighting. Ported from the reference implementation's pattern
    /// manager: one set of patterns, used consistently everywhere.
    /// </summary>
    public static class LogPatterns
    {
        // Multiline so ^ anchors at the start of each line inside a joined
        // multi-line message (stack-trace frames in message continuations).
        private const RegexOptions Options =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline;

        /// <summary>RimWorld log timestamps, e.g. "11:04:37 PM".</summary>
        public static readonly Regex Timestamp = new(@"\d{1,2}:\d{2}:\d{2} [AP]M", Options);

        public static readonly Regex Info =
            new(@"\b(info|initializ(?:ed|ing)|loaded|start(?:ed|ing)?|done|success)\b", Options);

        public static readonly Regex Keybind = new(@"key binding conflict", Options);

        /// <summary>Bracketed mod attribution lines that also carry a problem word.</summary>
        public static readonly Regex ModIssue =
            new(@"(\[.*\].*(error|warning|exception))|(mod.*(conflict|issue))", Options);

        public static readonly Regex Warning =
            new(@"\b(warning|warn|deprecat|deprecated)\b|\[W\]", Options);

        public static readonly Regex Error =
            new(@"\b(error|failed|fatal|critical)\b|\[E\]", Options);

        public static readonly Regex Exception = new(@"exception", Options);

        /// <summary>Managed stack-trace frames, e.g. "   at Verse.Awake ()".</summary>
        public static readonly Regex StackTrace = new(@"^\s*at ", Options);

        /// <summary>
        /// Windows or Unix filesystem paths appearing inside log text; used by the
        /// "open this path" context action.
        /// </summary>
        public static readonly Regex PathLike = new(
            @"(?:[a-zA-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+)" +
            @"|(?:/(?:[^/\s]+/)*[^/\s]+)",
            Options);
    }
}
