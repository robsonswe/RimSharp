using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    public enum ModReplacementDialogResult
    {
        Cancel,
        Download
    }

    public class ReplacementSelectionViewModel : ViewModelBase
    {
        public ModItem OriginalMod { get; }
        public ModReplacementInfo ReplacementInfo { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public ReplacementSelectionViewModel(ModItem original, ModReplacementInfo replacement)
        {
            OriginalMod = original;
            ReplacementInfo = replacement;
            IsSelected = true; // Default to selected
        }
    }


    public class ModReplacementDialogViewModel : DialogViewModelBase<ModReplacementDialogResult>
    {
        private ObservableCollection<ModReplacementItem> _replacements = new ObservableCollection<ModReplacementItem>();
        public ObservableCollection<ModReplacementItem> Replacements
        {
            get => _replacements;
            set
            {
                // Unsubscribe from old collection if necessary
                if (_replacements != null)
                {
                    foreach (var item in _replacements)
                    {
                        item.PropertyChanged -= ReplacementItem_PropertyChanged;
                    }
                }

                if (SetProperty(ref _replacements, value))
                {
                    // Subscribe to new collection
                    if (_replacements != null)
                    {
                        foreach (var item in _replacements)
                        {
                            item.PropertyChanged += ReplacementItem_PropertyChanged;
                        }
                        UpdateSelectedCount(); // Initial count
                    }
                }
            }
        }


        // New properties for the already installed replacements
        private ObservableCollection<ModReplacementItem> _alreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>();
        public ObservableCollection<ModReplacementItem> AlreadyInstalledReplacements
        {
            get => _alreadyInstalledReplacements;
            set => SetProperty(ref _alreadyInstalledReplacements, value);
        }

        // Flag to determine if we show the Already Installed section
        private bool _hasAlreadyInstalledReplacements;
        public bool HasAlreadyInstalledReplacements
        {
            get => _hasAlreadyInstalledReplacements;
            private set => SetProperty(ref _hasAlreadyInstalledReplacements, value);
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value); // Make setter private
        }

        private ICommand _selectAllCommand;
        public ICommand SelectAllCommand => _selectAllCommand ??= CreateCommand(SelectAll);

        private ICommand _selectNoneCommand;
        public ICommand SelectNoneCommand => _selectNoneCommand ??= CreateCommand(SelectNone);

        public ModReplacementDialogViewModel(
            IEnumerable<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate)> replacements,
            IEnumerable<ModItem> allInstalledMods)
            : base("Mod Replacements Available")
        {
            // --- FIXED LOOKUP LOGIC ---
            // Create a lookup for all installed mods by their unique Steam ID.
            var installedModsBySteamId = new HashSet<string>(
                allInstalledMods
                    .Where(m => !string.IsNullOrEmpty(m.SteamId))
                    .Select(m => m.SteamId),
                StringComparer.OrdinalIgnoreCase);

            // Create a separate lookup for installed mods that DO NOT have a Steam ID, keyed by their PackageID.
            // This explicitly prevents a Steam mod's PackageID from causing a false positive match.
            var installedLocalModsByPackageId = new HashSet<string>(
                allInstalledMods
                    .Where(m => string.IsNullOrEmpty(m.SteamId) && !string.IsNullOrEmpty(m.PackageId))
                    .Select(m => m.PackageId),
                StringComparer.OrdinalIgnoreCase);
            // --- END FIXED LOOKUP LOGIC ---

            var itemGroups = replacements
                .Select(r =>
                {
                    var replacementInfo = r.Replacement;
                    bool alreadyInstalled = false;

                    // --- ACCURATE INSTALLED CHECK ---
                    // 1. Prioritize check by the replacement's Steam ID. This is the unique identifier.
                    if (!string.IsNullOrEmpty(replacementInfo.ReplacementSteamId))
                    {
                        alreadyInstalled = installedModsBySteamId.Contains(replacementInfo.ReplacementSteamId);
                    }
                    // 2. If the replacement has NO Steam ID, it might be a local mod.
                    //    Check against our list of installed *local* mods using its Package ID.
                    else if (!string.IsNullOrEmpty(replacementInfo.ReplacementModId))
                    {
                        alreadyInstalled = installedLocalModsByPackageId.Contains(replacementInfo.ReplacementModId);
                    }
                    // --- END ACCURATE INSTALLED CHECK ---

                    return new ModReplacementItem
                    {
                        OriginalMod = r.Original,
                        ReplacementInfo = r.Replacement,
                        OriginalLastUpdate = r.OriginalUpdate,
                        ReplacementLastUpdate = r.ReplacementUpdate,
                        IsSelected = !alreadyInstalled,
                        ReplacementAlreadyInstalled = alreadyInstalled
                    };
                })
                .ToList();

            // Group into regular and already installed collections
            var regularItems = itemGroups
                .Where(item => !item.ReplacementAlreadyInstalled)
                .ToList();

            var alreadyInstalledItems = itemGroups
                .Where(item => item.ReplacementAlreadyInstalled)
                .ToList();

            // Subscribe to property change events for the regular items that can be selected/deselected
            foreach (var item in regularItems)
            {
                item.PropertyChanged += ReplacementItem_PropertyChanged;
            }

            // Set the collections
            Replacements = new ObservableCollection<ModReplacementItem>(regularItems);
            AlreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>(alreadyInstalledItems);
            HasAlreadyInstalledReplacements = alreadyInstalledItems.Count > 0;
        }

        private void ReplacementItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModReplacementItem.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        private void SelectAll()
        {
            foreach (var item in Replacements)
            {
                item.IsSelected = true;
            }
        }

        private void SelectNone()
        {
            foreach (var item in Replacements)
            {
                item.IsSelected = false;
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedCount = Replacements.Count(r => r.IsSelected);
        }

        public List<ReplacementSelectionViewModel> GetSelectedReplacements()
        {
            return Replacements
                .Where(r => r.IsSelected)
                .Select(r => new ReplacementSelectionViewModel(r.OriginalMod, r.ReplacementInfo))
                .ToList();
        }
    }
}