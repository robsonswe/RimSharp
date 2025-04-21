#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using System.Linq; // Needed for Join

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public class MissingModVariantViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private string _authors = string.Empty;
        private string _versionsString = string.Empty;
        private string _steamId = string.Empty;
        // private bool _isPublished = false; // No backing field needed if get-only

        public string Name
        {
            get => _name;
            private set => SetProperty(ref _name, value); // Keep private set if needed internally
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

        public string SteamId { get; } // Keep get-only

        /// <summary>
        /// Indicates whether the mod variant is currently published on Steam Workshop.
        /// </summary>
        public bool IsPublished { get; } // Make this get-only, set in constructor

        /// <summary>
        /// Helper property for XAML binding, indicates if this variant can be selected.
        /// </summary>
        public bool IsSelectable => IsPublished;

        // Constructor to populate from ModDictionaryEntry
        public MissingModVariantViewModel(ModDictionaryEntry entry)
        {
            Name = entry.Name ?? "Unknown Name";
            Authors = entry.Authors ?? "Unknown Author";
            VersionsString = entry.Versions != null && entry.Versions.Any()
                            ? string.Join(", ", entry.Versions)
                            : "Unknown Version";
            SteamId = entry.SteamId; // Assign only in constructor
            IsPublished = entry.Published; // Assign the Published status
        }
    }
}
