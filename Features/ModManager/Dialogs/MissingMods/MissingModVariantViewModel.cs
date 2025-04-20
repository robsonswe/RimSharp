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

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Authors
        {
            get => _authors;
            set => SetProperty(ref _authors, value);
        }

        public string VersionsString
        {
            get => _versionsString;
            set => SetProperty(ref _versionsString, value);
        }

        public string SteamId
        {
            get => _steamId;
            // REMOVE the private set if it was there, ensure it's truly get-only
            // private set => SetProperty(ref _steamId, value);
        }

        // Constructor to populate from ModDictionaryEntry
        public MissingModVariantViewModel(ModDictionaryEntry entry)
        {
            Name = entry.Name ?? "Unknown Name";
            Authors = entry.Authors ?? "Unknown Author";
            VersionsString = entry.Versions != null && entry.Versions.Any()
                            ? string.Join(", ", entry.Versions)
                            : "Unknown Version";
            _steamId = entry.SteamId; // Assign only in constructor
        }
    }
}
