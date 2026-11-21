using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.AppDir.AppFiles;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Mods.Validation.Duplicates
{
    public class ModItemWrapper : ViewModelBase
    {
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    if (value)
                    {
                        ParentGroup?.UpdateSelection(this);
                        Original.IsActive = true;
                    }
                    else
                    {
                        Original.IsActive = false;
                    }
                    ParentGroup?.NotifyOutcomesChanged();
                }
            }
        }

        private bool _isOriginal;
        public bool IsOriginal
        {
            get => _isOriginal;
            set => SetProperty(ref _isOriginal, value);
        }

        public bool WasOriginallyActive { get; }
        public ModItem Original { get; }
        public DuplicateModGroupViewModel ParentGroup { get; }

        public string Name => Original.DisplayName;
        public string Authors => Original.Authors;
        public string ModVersion => Original.ModVersion;
        public string Path => Original.Path;
        public string SteamId => Original.SteamId;
        public string Url => Original.Url;
        public string SteamUrl => Original.SteamUrl;
        public string ExternalUrl => Original.ExternalUrl;

        public bool HasSteamId => !string.IsNullOrWhiteSpace(SteamId);

        public IReadOnlyList<string> VersionStrings =>
            Original.SupportedVersions?
                .Select(v => v.Version)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

        public bool HasVersions => VersionStrings.Count > 0;

        public string SupportedVersions => Original.SupportedVersions != null && Original.SupportedVersions.Any()
            ? string.Join(", ", Original.SupportedVersions.Select(v => v.ToString()))
            : "Unknown";

        public bool IsKept => !ParentGroup.IsIgnored && IsActive;
        public bool WillBeDeleted => !ParentGroup.IsIgnored && !IsActive;
        public bool IsKeptBecauseIgnored => ParentGroup.IsIgnored;

        public string OutcomeText => IsKeptBecauseIgnored
            ? "Kept (Ignored)"
            : IsKept ? "Keeping" : "Will be deleted";

        public ModItemWrapper(ModItem original, DuplicateModGroupViewModel parentGroup)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));
            ParentGroup = parentGroup ?? throw new ArgumentNullException(nameof(parentGroup));
            _isActive = original.IsActive;
            WasOriginallyActive = original.IsActive;
        }

        internal void NotifyOutcomeChanged()
        {
            OnPropertyChanged(nameof(IsKept));
            OnPropertyChanged(nameof(WillBeDeleted));
            OnPropertyChanged(nameof(IsKeptBecauseIgnored));
            OnPropertyChanged(nameof(OutcomeText));
        }
    }
}