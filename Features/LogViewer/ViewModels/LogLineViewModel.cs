#nullable enable
using RimSharp.Features.LogViewer.Services;

namespace RimSharp.Features.LogViewer.ViewModels
{
    /// <summary>
    /// One displayed log line. Immutable — lines never change after being added,
    /// so plain properties are enough for binding.
    /// </summary>
    public sealed class LogLineViewModel
    {
        public string Text { get; }
        public LogLineSeverity Severity { get; }

        /// <summary>Theme brush key the view resolves for this line's severity.</summary>
        public string SeverityBrushKey => Severity switch
        {
            LogLineSeverity.Error or LogLineSeverity.Exception => "BrushTextError",
            LogLineSeverity.Warning or LogLineSeverity.ModIssue or LogLineSeverity.Keybind => "BrushTextWarning",
            _ => "BrushTextMuted"
        };

        public LogLineViewModel(string text, LogLineSeverity severity)
        {
            Text = text;
            Severity = severity;
        }
    }
}
