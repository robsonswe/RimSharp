#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using System;
using System.Globalization;
using System.Linq;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModVariantViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private string _authors = string.Empty;
        private string _versionsString = string.Empty;
        private string _steamId = string.Empty;
        private bool _isSelected;
        private string _groupName = string.Empty;
        private long _lastUpdatedTimestamp;
        private bool _isPublished;

        public string Name
        {
            get => _name;
            private set => SetProperty(ref _name, value);
        }

        public string Authors
        {
            get => _authors;
            private set => SetProperty(ref _authors, value);
        }

        public string VersionsString
        {
            get => _versionsString;
            private set => SetProperty(ref _versionsString, value);
        }

        public string SteamId { get; }

        /// <summary>
        /// Whether the workshop item is still published.
        /// </summary>
        public bool IsPublished
        {
            get => _isPublished;
            private set
            {
                if (SetProperty(ref _isPublished, value))
                {
                    OnPropertyChanged(nameof(IsSelectable));
                    OnPropertyChanged(nameof(SelectableOpacity));
                }
            }
        }

        /// <summary>
        /// Whether the user can select this variant (only published mods are selectable).
        /// </summary>
        public bool IsSelectable => IsPublished;

        /// <summary>
        /// Whether this variant is currently selected via radio button.
        /// Two-way bound from the RadioButton.IsChecked in the view.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// RadioButton group name — set by the parent group so all variants
        /// within the same packageId share one group.
        /// </summary>
        public string GroupName
        {
            get => _groupName;
            set => SetProperty(ref _groupName, value);
        }

        /// <summary>
        /// Opacity for the variant card: full for selectable, dimmed for unpublished.
        /// </summary>
        public double SelectableOpacity => IsSelectable ? 1.0 : 0.5;

        /// <summary>
        /// Unix timestamp (seconds) of the last Steam Workshop update.
        /// Set to 0 when unknown.
        /// </summary>
        public long LastUpdatedTimestamp
        {
            get => _lastUpdatedTimestamp;
            set
            {
                if (SetProperty(ref _lastUpdatedTimestamp, value))
                {
                    OnPropertyChanged(nameof(LastUpdatedDisplay));
                    OnPropertyChanged(nameof(HasLastUpdated));
                }
            }
        }

        /// <summary>
        /// Whether a last-updated date is available.
        /// </summary>
        public bool HasLastUpdated => LastUpdatedTimestamp > 0;

        /// <summary>
        /// Formatted display string for the last-updated date, e.g. "Updated: 15 Mar 2025".
        /// </summary>
        public string LastUpdatedDisplay =>
            LastUpdatedTimestamp > 0
                ? $"Updated: {DateTimeOffset.FromUnixTimeSeconds(LastUpdatedTimestamp).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                : string.Empty;

        // Constructor to populate from ModDictionaryEntry
        public MissingModVariantViewModel(
            ModDictionaryEntry entry,
            long lastUpdatedTimestamp = 0,
            string? displayName = null,
            string? versionsString = null,
            bool? isPublished = null)
        {
            Name = !string.IsNullOrWhiteSpace(displayName) ? displayName : (entry.Name ?? "Unknown Name");
            Authors = entry.Authors ?? "Unknown Author";
            VersionsString = !string.IsNullOrWhiteSpace(versionsString)
                ? versionsString
                : (entry.Versions != null && entry.Versions.Any()
                    ? string.Join(", ", entry.Versions)
                    : "Unknown Version");
            SteamId = entry.SteamId;
            _isPublished = isPublished ?? entry.Published;
            LastUpdatedTimestamp = lastUpdatedTimestamp;
        }
    }
}
