using RimSharp.Infrastructure.Mods.Validation.Duplicates;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public class DuplicateModGroupViewModel : ViewModelBase
    {
        public ObservableCollection<ModItemWrapper> Mods { get; } = new ObservableCollection<ModItemWrapper>();
        public string PackageId { get; }
        public string GroupName { get; }

        private bool _isIgnored;
        public bool IsIgnored
        {
            get => _isIgnored;
            set
            {
                if (SetProperty(ref _isIgnored, value))
                {
                    if (value)
                    {
                        foreach (var mod in Mods)
                        {
                            mod.IsActive = false;
                        }
                    }
                }
            }
        }

        public DuplicateModGroupViewModel(IGrouping<string, ModItem> group)
        {
            PackageId = group.Key;
            GroupName = $"DuplicateGroup_{group.GetHashCode()}";
            _isIgnored = false;

            foreach (var mod in group)
            {
                if (mod != null)
                {
                    var wrapper = new ModItemWrapper(mod, this);
                    Mods.Add(wrapper);
                }
            }
            // Determine the original mod based on the lowest SteamId, if possible.
            // 1. Check if ALL mods in the group have a valid, non-empty SteamId.
            bool allHaveSteamId = Mods.All(w => !string.IsNullOrEmpty(w.SteamId) && long.TryParse(w.SteamId, out _));

            if (allHaveSteamId)
            {
                // 2. If so, find the one with the minimum SteamId using LINQ.
                var originalModWrapper = Mods
                    .OrderBy(w => long.Parse(w.SteamId))
                    .FirstOrDefault();

                // 3. Set the IsOriginal flag on that wrapper.
                if (originalModWrapper != null)
                {
                    originalModWrapper.IsOriginal = true;
                }
            }
            
            // Updated to work with the new VersionSupport type
            var defaultMod = group.OrderByDescending(m => m?.IsActive ?? false)
                  .ThenByDescending(m => m?.SupportedVersions?.FirstOrDefault()?.Version)
                  .FirstOrDefault();

            if (defaultMod != null)
            {
                var wrapper = Mods.FirstOrDefault(w => w?.Original == defaultMod);
                if (wrapper != null)
                {
                    wrapper.IsActive = true;
                }
            }
        }

        public ModItem GetSelectedModToKeep()
        {
            return Mods.FirstOrDefault(m => m?.IsActive == true)?.Original;
        }

        public void UpdateSelection(ModItemWrapper selected)
        {
            IsIgnored = false;

            foreach (var mod in Mods)
            {
                if (mod != null && mod != selected)
                {
                    mod.IsActive = false;
                }
            }
        }
    }
}