using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
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
            IEnumerable<(ModItem Original, ModReplacementInfo Replacement)> replacements,
            IEnumerable<ModItem> allInstalledMods) // Keep original signature
            : base("Mod Replacements Available")
        {
            // --- IMPROVED LOOKUP ---
            // Create HashSet for efficient, case-insensitive lookups of installed mod IDs
            var installedSteamIds = new HashSet<string>(
                allInstalledMods
                    .Where(m => !string.IsNullOrEmpty(m.SteamId))
                    .Select(m => m.SteamId),
                StringComparer.OrdinalIgnoreCase);

            var installedPackageIds = new HashSet<string>(
                allInstalledMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId))
                    .Select(m => m.PackageId),
                StringComparer.OrdinalIgnoreCase);
            // --- END IMPROVED LOOKUP ---

            var itemGroups = replacements
                .Select(r =>
                {
                    // --- REVISED INSTALLED CHECK ---
                    // Prioritize Steam ID check for the REPLACEMENT mod
                    bool installedBySteamId = !string.IsNullOrEmpty(r.Replacement.ReplacementSteamId) &&
                                              installedSteamIds.Contains(r.Replacement.ReplacementSteamId);

                    // If not found by Steam ID, check by Package ID for the REPLACEMENT mod
                    // Note: This is less reliable due to non-unique PackageIds, but acts as a fallback
                    bool installedByPackageId = !installedBySteamId &&
                                                !string.IsNullOrEmpty(r.Replacement.ReplacementModId) &&
                                                installedPackageIds.Contains(r.Replacement.ReplacementModId);

                    bool alreadyInstalled = installedBySteamId || installedByPackageId;
                    // --- END REVISED INSTALLED CHECK ---

                    return new ModReplacementItem
                    {
                        OriginalMod = r.Original,
                        ReplacementInfo = r.Replacement,
                        IsSelected = !alreadyInstalled, // Only select by default if not already installed
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
            // Important: Assign to the property to trigger the setter logic (event handling, count update)
            Replacements = new ObservableCollection<ModReplacementItem>(regularItems);
            AlreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>(alreadyInstalledItems);
            HasAlreadyInstalledReplacements = alreadyInstalledItems.Count > 0;

            // Initial count update is handled by the Replacements property setter
            // UpdateSelectedCount(); // This call is redundant now if setter calls it
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
                item.IsSelected = true; // PropertyChanged triggers count update via event handler
            }
        }

        private void SelectNone()
        {
            foreach (var item in Replacements)
            {
                item.IsSelected = false; // PropertyChanged triggers count update via event handler
            }
        }

        private void UpdateSelectedCount()
        {
            // Recalculate count from the collection that allows selection
            SelectedCount = Replacements.Count(r => r.IsSelected);
        }

        public List<ReplacementSelectionViewModel> GetSelectedReplacements()
        {
            // Get selected items from the main list
            return Replacements
                .Where(r => r.IsSelected)
                .Select(r => new ReplacementSelectionViewModel(r.OriginalMod, r.ReplacementInfo))
                .ToList();
        }
    }
}
