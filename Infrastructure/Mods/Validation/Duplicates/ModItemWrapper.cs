using System;
using System.Linq;
using RimSharp.MyApp.AppFiles;
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
                }
            }
        }

        public ModItem Original { get; }
        public DuplicateModGroupViewModel ParentGroup { get; }

        public string Name => Original?.Name;
        public string Authors => Original?.Authors;
        public string ModVersion => Original?.ModVersion;
        public string Path => Original?.Path;
        public string SteamId => Original?.SteamId;
        public string Url => Original?.Url;
        public string SteamUrl => Original?.SteamUrl;
        public string ExternalUrl => Original?.ExternalUrl;

        public string SupportedVersions => Original?.SupportedVersions != null && Original.SupportedVersions.Any()
            ? string.Join(", ", Original.SupportedVersions)
            : "Unknown";

        public ModItemWrapper(ModItem original, DuplicateModGroupViewModel parentGroup)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));
            ParentGroup = parentGroup ?? throw new ArgumentNullException(nameof(parentGroup));
            _isActive = original.IsActive;
        }
    }
}