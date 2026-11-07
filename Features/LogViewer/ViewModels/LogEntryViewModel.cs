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
        public string Text { get; }
        public LogLineSeverity Severity => Classification.Severity;

        /// <summary>Full category membership for this message (filters and stats read this).</summary>
        public LogLineClassification Classification { get; }

        /// <summary>Theme brush key the view resolves for this message's severity.</summary>
        public string SeverityBrushKey => Severity switch
        {
            LogLineSeverity.Error or LogLineSeverity.Exception => "BrushTextError",
            LogLineSeverity.Warning or LogLineSeverity.ModIssue or LogLineSeverity.Keybind => "BrushTextWarning",
            _ => "BrushTextMuted"
        };

        public LogEntryViewModel(string text, LogLineClassification classification)
        {
            Text = text;
            Classification = classification;
        }
    }
}
