using RimSharp.Features.LogViewer.Services;

namespace RimSharp.Features.LogViewer.ViewModels
{
    /// <summary>
    /// One displayed log message: the lead line plus any indented continuation
    /// lines, joined so the message reads, filters and counts as a whole.
    /// Immutable — messages never change after being added, so plain
    /// properties are enough for binding.
    /// </summary>
    public sealed class LogEntryViewModel
    {
        public LogEntryViewModel(string text, LogLineClassification classification, int repeatCount = 1)
        {
            Text = text;
            Classification = classification;
            RepeatCount = repeatCount < 1 ? 1 : repeatCount;
        }

        public string Text { get; }
        public LogLineSeverity Severity => Classification.Severity;

        /// <summary>Full category membership for this message (filters and stats read this).</summary>
        public LogLineClassification Classification { get; }

        /// <summary>
        /// How many consecutive occurrences of this message collapsed into this
        /// entry (1 when nothing collapsed). Filters and statistics still read
        /// <see cref="Text"/>; only the annotation grows.
        /// </summary>
        public int RepeatCount { get; }

        /// <summary>
        /// What the list displays: the message text, annotated with the repeat
        /// count when a run of identical messages collapsed into this entry.
        /// </summary>
        public string DisplayText => RepeatCount > 1
            ? Text + "\n(Repeated " + RepeatCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " times)"
            : Text;

        /// <summary>Same message one repeat longer — the collapsed run keeps one entry.</summary>
        public LogEntryViewModel WithRepeat() => new(Text, Classification, RepeatCount + 1);

        /// <summary>Theme brush key the view resolves for this message's severity.</summary>
        public string SeverityBrushKey => Severity switch
        {
            LogLineSeverity.Error or LogLineSeverity.Exception => "BrushTextError",
            LogLineSeverity.Warning or LogLineSeverity.ModIssue or LogLineSeverity.Keybind => "BrushTextWarning",
            _ => "BrushTextMuted"
        };
    }
}
