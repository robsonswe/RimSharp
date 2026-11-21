#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModVariantViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private string _authors = string.Empty;
        private string _versionsString = string.Empty;
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
            private set
            {
                if (SetProperty(ref _versionsString, value))
                {
                    OnPropertyChanged(nameof(VersionChips));
                    OnPropertyChanged(nameof(HasVersionChips));
                }
            }
        }

        public IReadOnlyList<string> VersionChips =>
            string.IsNullOrWhiteSpace(VersionsString) ||
            string.Equals(VersionsString, "Unknown Version", StringComparison.OrdinalIgnoreCase)
                ? Array.Empty<string>()
                : VersionsString
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

        public bool HasVersionChips => VersionChips.Count > 0;

        public string SteamId { get; }

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

        public bool IsSelectable => IsPublished;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string GroupName
        {
            get => _groupName;
            set => SetProperty(ref _groupName, value);
        }

        public double SelectableOpacity => IsSelectable ? 1.0 : 0.5;

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

        public bool HasLastUpdated => LastUpdatedTimestamp > 0;

        public string LastUpdatedDisplay =>
            LastUpdatedTimestamp > 0
                ? $"Updated: {DateTimeOffset.FromUnixTimeSeconds(LastUpdatedTimestamp).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                : string.Empty;

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