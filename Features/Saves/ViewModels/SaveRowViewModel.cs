#nullable enable
using System;
using System.Globalization;
using RimSharp.Services.ModManagement;

namespace RimSharp.Features.Saves.ViewModels
{
    /// <summary>
    /// One row of the saves list: the file facts (name, size, modified).
    /// The row is immutable - it shows what the file system already knows and
    /// nothing else. The compatibility verdict lives in the details panel of
    /// the selected save, never in the list.
    /// </summary>
    public sealed class SaveRowViewModel
    {
        public SaveRowViewModel(SaveFileEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            Name = entry.Name;
            Path = entry.Path;
            SizeText = FormatSize(entry.SizeBytes);
            ModifiedText = entry.LastModifiedUtc.ToLocalTime()
                .ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);
            SecondaryText = $"{SizeText} · modified {ModifiedText}";
        }

        /// <summary>The save name, without the file extension.</summary>
        public string Name { get; }

        /// <summary>The full path of the save file.</summary>
        public string Path { get; }

        /// <summary>The file size, e.g. "12.4 MB".</summary>
        public string SizeText { get; }

        /// <summary>The last write time, e.g. "Nov 9, 2026, 3:14 PM".</summary>
        public string ModifiedText { get; }

        /// <summary>The secondary line of the row: "{size} · modified {date}".</summary>
        public string SecondaryText { get; }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            double kilobytes = bytes / 1024.0;
            if (kilobytes < 1024)
            {
                return $"{kilobytes:0.#} KB";
            }

            double megabytes = kilobytes / 1024.0;
            if (megabytes < 1024)
            {
                return $"{megabytes:0.#} MB";
            }

            double gigabytes = megabytes / 1024.0;
            return $"{gigabytes:0.##} GB";
        }
    }
}
