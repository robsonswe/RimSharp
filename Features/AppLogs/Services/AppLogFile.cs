#nullable enable
using System;

namespace RimSharp.Features.AppLogs.Services
{
    /// <summary>The severity the app's own logger tags each line with.</summary>
    public enum AppLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// One log file the app wrote: a service's log for one day. Module names
    /// may contain underscores, so the date is the trailing 8 digits and the
    /// module is everything between the prefix and that date.
    /// </summary>
    public sealed record AppLogFile(string Module, DateTime Date, string Path, long SizeBytes, DateTime LastWriteTime);

    /// <summary>
    /// One message read from an app log: the tagged lead line plus every
    /// continuation line (exception text and stack traces arrive as bare
    /// lines with no tag of their own, so they belong to the message above).
    /// </summary>
    public sealed record AppLogEntry(DateTime Timestamp, AppLogLevel Level, string Module, string Text)
    {
        /// <summary>Lines the message occupied in the file, tag line included.</summary>
        public int LineCount { get; init; }

        /// <summary>True for the two severities that mean something broke.</summary>
        public bool IsProblem => Level is AppLogLevel.Error or AppLogLevel.Critical;
    }
}
