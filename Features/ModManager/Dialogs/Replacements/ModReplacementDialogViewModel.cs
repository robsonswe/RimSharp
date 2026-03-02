using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
            OriginalMod = original ?? throw new ArgumentNullException(nameof(original));
            ReplacementInfo = replacement ?? throw new ArgumentNullException(nameof(replacement));
            IsSelected = true; // Default to selected
        }
    }

public class ModReplacementDialogViewModel : DialogViewModelBase<ModReplacementDialogResult>
    {
        private ObservableCollection<ModReplacementItem>? _replacements = new ObservableCollection<ModReplacementItem>();
        public ObservableCollection<ModReplacementItem>? Replacements
        {
            get => _replacements;
            set
            {

                if (_replacements != null)
                {
                    foreach (var item in _replacements)
                    {
                        if (item != null)
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
                            if (item != null)
                                item.PropertyChanged += ReplacementItem_PropertyChanged;
                        }
                        UpdateSelectedCount(); // Initial count
                    }
                }
            }
        }
        private ObservableCollection<ModReplacementItem> _alreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>();
        public ObservableCollection<ModReplacementItem> AlreadyInstalledReplacements
        {
            get => _alreadyInstalledReplacements;
            set => SetProperty(ref _alreadyInstalledReplacements, value);
        }

        private bool _hasAlreadyInstalledReplacements;
        public bool HasAlreadyInstalledReplacements
        {
            get => _hasAlreadyInstalledReplacements;
            private set => SetProperty(ref _hasAlreadyInstalledReplacements, value);
        }

        private bool _hasRegularReplacements;
        public bool HasRegularReplacements
        {
            get => _hasRegularReplacements;
            private set => SetProperty(ref _hasRegularReplacements, value);
        }

        public bool HasAnyReplacements => HasRegularReplacements || HasAlreadyInstalledReplacements;

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value); // Make setter private
        }

        private ICommand? _selectAllCommand;
        public ICommand SelectAllCommand => _selectAllCommand ??= CreateCommand(SelectAll);

        private ICommand? _selectNoneCommand;
        public ICommand SelectNoneCommand => _selectNoneCommand ??= CreateCommand(SelectNone);

        public ModReplacementDialogViewModel(
            IEnumerable<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate)> replacements,
            IEnumerable<ModItem> allInstalledMods)
            : base("Mod Replacements Available")
        {

            var installedModsBySteamId = new HashSet<string>(
                allInstalledMods
                    .Where(m => !string.IsNullOrEmpty(m.SteamId))
                    .Select(m => m.SteamId),
                StringComparer.OrdinalIgnoreCase);

var installedLocalModsByPackageId = new HashSet<string>(
                allInstalledMods
                    .Where(m => string.IsNullOrEmpty(m.SteamId) && !string.IsNullOrEmpty(m.PackageId))
                    .Select(m => m.PackageId),
                StringComparer.OrdinalIgnoreCase);

            var itemGroups = replacements
                .Select(r =>
                {
                    var replacementInfo = r.Replacement;
                    bool alreadyInstalled = false;

                    if (!string.IsNullOrEmpty(replacementInfo.ReplacementSteamId))
                    {
                        alreadyInstalled = installedModsBySteamId.Contains(replacementInfo.ReplacementSteamId);
                    }

else if (!string.IsNullOrEmpty(replacementInfo.ReplacementModId))
                    {
                        alreadyInstalled = installedLocalModsByPackageId.Contains(replacementInfo.ReplacementModId);
                    }

                    return new ModReplacementItem(r.Original, r.Replacement)
                    {
                        OriginalLastUpdate = r.OriginalUpdate,
                        ReplacementLastUpdate = r.ReplacementUpdate,
                        IsSelected = !alreadyInstalled,
                        ReplacementAlreadyInstalled = alreadyInstalled
                    };
                })
                .ToList();

            var regularItems = itemGroups
                .Where(item => !item.ReplacementAlreadyInstalled)
                .ToList();

            var alreadyInstalledItems = itemGroups
                .Where(item => item.ReplacementAlreadyInstalled)
                .ToList();

            Debug.WriteLine($"[ModReplacementVM] Found {regularItems.Count} regular and {alreadyInstalledItems.Count} already installed replacements.");

            foreach (var item in regularItems)
            {
                item.PropertyChanged += ReplacementItem_PropertyChanged;
            }
            Replacements = new ObservableCollection<ModReplacementItem>(regularItems);
            AlreadyInstalledReplacements = new ObservableCollection<ModReplacementItem>(alreadyInstalledItems);

            HasRegularReplacements = regularItems.Count > 0;
            HasAlreadyInstalledReplacements = alreadyInstalledItems.Count > 0;
        }

        private void ReplacementItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModReplacementItem.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        private void SelectAll()
        {
            if (Replacements == null) return;
            foreach (var item in Replacements)
            {
                item.IsSelected = true;
            }
        }

        private void SelectNone()
        {
            if (Replacements == null) return;
            foreach (var item in Replacements)
            {
                item.IsSelected = false;
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedCount = Replacements?.Count(r => r.IsSelected) ?? 0;
        }

        public List<ReplacementSelectionViewModel> GetSelectedReplacements()
        {
            return Replacements?
                .Where(r => r.IsSelected)
                .Select(r => new ReplacementSelectionViewModel(r.OriginalMod, r.ReplacementInfo))
                .ToList() ?? new List<ReplacementSelectionViewModel>();
        }
    }
}


