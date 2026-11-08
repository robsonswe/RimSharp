#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using RimSharp.Features.AppLogs.Services;

namespace RimSharp.Features.AppLogs.ViewModels
{
    /// <summary>
    /// One displayed message: the tagged lead line plus its continuation
    /// lines, joined so the message reads, filters and counts as a whole.
    /// Immutable — messages never change once added.
    /// </summary>
    public sealed class AppLogEntryViewModel
    {
        public AppLogEntryViewModel(AppLogEntry entry, int repeatCount = 1)
        {
            Entry = entry;
            RepeatCount = repeatCount < 1 ? 1 : repeatCount;
        }

        public AppLogEntry Entry { get; }

        public DateTime Timestamp => Entry.Timestamp;

        public AppLogLevel Level => Entry.Level;

        public string Module => Entry.Module;

        public int RepeatCount { get; }

        /// <summary>
        /// What the list displays: the message text, annotated with the
        /// repeat count when a run of identical messages collapsed.
        /// </summary>
        public string DisplayText => RepeatCount > 1
            ? Entry.Text + "\n(Repeated " + RepeatCount.ToString(CultureInfo.InvariantCulture) + " times)"
            : Entry.Text;

        /// <summary>Same message one repeat longer — the collapsed run keeps one entry.</summary>
        public AppLogEntryViewModel WithRepeat() => new(Entry, RepeatCount + 1);

        /// <summary>Theme brush key the view resolves for this message's severity.</summary>
        public string SeverityBrushKey => Level switch
        {
            AppLogLevel.Error or AppLogLevel.Critical => "BrushTextError",
            AppLogLevel.Warning => "BrushTextWarning",
            _ => "BrushTextMuted"
        };
    }

    /// <summary>A filter option for the level combo: null level means "All".</summary>
    public sealed record AppLogLevelFilter(string Label, AppLogLevel? Level);

    /// <summary>One service in the module list: its log files and their sizes.</summary>
    public sealed class AppLogModuleItem
    {
        public AppLogModuleItem(string module, IReadOnlyList<AppLogFile> files)
        {
            Module = module;
            Files = files;
        }

        public string Module { get; }

        public IReadOnlyList<AppLogFile> Files { get; }

        /// <summary>The file a selection loads: the service's newest day.</summary>
        public AppLogFile Newest => Files[0];

        public int FileCount => Files.Count;

        public string Header => Module == "General"
            ? "General (unclassified messages)"
            : Module;

        public string SubHeader
        {
            get
            {
                var day = Newest.Date.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
                return FileCount > 1
                    ? $"{day} (+{FileCount - 1} older day{(FileCount > 2 ? "s" : "")})"
                    : day;
            }
        }

        public long TotalBytes
        {
            get
            {
                var total = 0L;
                foreach (var file in Files)
                {
                    total += file.SizeBytes;
                }

                return total;
            }
        }
    }
}
