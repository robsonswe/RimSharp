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
            set => SetProperty(ref _isSelected, value); // This should trigger PropertyChanged for command CanExecute updates
        }

        public ReplacementSelectionViewModel(ModItem original, ModReplacementInfo replacement)
        {
            OriginalMod = original;
            ReplacementInfo = replacement;
            IsSelected = true; // Default to selected, or false if you prefer
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
            IEnumerable<ModItem> allInstalledMods)
            : base("Mod Replacements Available")
        {
            // Separate into two groups:
            // 1. Replacements that need to be downloaded
            // 2. Replacements that are already installed

            var installedModIdsLookup = allInstalledMods
                .ToDictionary(
                    m => m.PackageId?.ToLower() ?? string.Empty,
                    m => m,
                    StringComparer.OrdinalIgnoreCase);

            var itemGroups = replacements
                .Select(r => 
                {
                    // Try to find if replacement is already installed by PackageId
                    var replacementModId = r.Replacement.ReplacementModId?.ToLower() ?? string.Empty;
                    bool alreadyInstalled = !string.IsNullOrEmpty(replacementModId) && 
                                        installedModIdsLookup.ContainsKey(replacementModId);
                
                    // If not found by mod ID, try by Steam ID
                    if (!alreadyInstalled && !string.IsNullOrEmpty(r.Replacement.ReplacementSteamId))
                    {
                        alreadyInstalled = allInstalledMods.Any(m => 
                            string.Equals(m.SteamId, r.Replacement.ReplacementSteamId, 
                                         StringComparison.OrdinalIgnoreCase));
                    }

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

            // Subscribe to property change events for the regular items
            foreach (var item in regularItems)
            {
                item.PropertyChanged += ReplacementItem_PropertyChanged;
            }

            // Set the collections
            Replacements = new ObservableCollection<ModReplacementItem>(regularItems);
            AlreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>(alreadyInstalledItems);
            HasAlreadyInstalledReplacements = alreadyInstalledItems.Count > 0;

            // UpdateSelectedCount() will be called by the Replacements setter
        }

        private void ReplacementItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModReplacementItem.IsSelected))
            {
                // Recalculate count when an item's selection changes
                UpdateSelectedCount();
            }
        }

        private void SelectAll()
        {
            // Update IsSelected; PropertyChanged event will trigger count update
            foreach (var item in Replacements)
            {
                item.IsSelected = true;
            }
        }

        private void SelectNone()
        {
            // Update IsSelected; PropertyChanged event will trigger count update
            foreach (var item in Replacements)
            {
                item.IsSelected = false;
            }
        }

        private void UpdateSelectedCount()
        {
            // Recalculate count and update the property
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