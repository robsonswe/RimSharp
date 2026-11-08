namespace RimSharp.Features.LogViewer.Services
{
    /// <summary>
    /// Display severity for a single log line. Ordered most-to-least severe;
    /// <see cref="LogLineClassifier"/> assigns the highest matching category.
    /// </summary>
    public enum LogLineSeverity
    {
        None,
        Info,
        Keybind,
        ModIssue,
        Warning,
        Error,
        Exception
    }
}
