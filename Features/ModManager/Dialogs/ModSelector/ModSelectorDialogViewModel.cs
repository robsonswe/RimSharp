using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using FuzzySharp;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.ModSelector
{
    public class ModSelectorDialogViewModel : DialogViewModelBase<ModItem?>
    {
        private readonly List<ModItem> _allMods;
        private string _searchText = string.Empty;
        private ModItem? _selectedMod;
        private const int FuzzySearchThreshold = 75;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        public ModItem? SelectedMod
        {
            get => _selectedMod;
            set => SetProperty(ref _selectedMod, value);
        }

        public ObservableCollection<ModItem> FilteredMods { get; } = new();

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public ModSelectorDialogViewModel(IEnumerable<ModItem> allMods) : base("Select Mod")
        {
            _allMods = allMods?.ToList() ?? new List<ModItem>();
            
            ConfirmCommand = CreateCommand(Confirm, () => SelectedMod != null, nameof(SelectedMod));
            CancelCommand = CreateCommand(Cancel);

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<ModItem> filtered;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                filtered = _allMods.OrderBy(m => m.Name);
            }
            else
            {
                string searchLower = SearchText.ToLowerInvariant();
                filtered = _allMods.Where(m =>
                    (m.Name?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (m.PackageId?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    Fuzz.TokenSetRatio(m.Name?.ToLowerInvariant() ?? "", searchLower) >= FuzzySearchThreshold
                ).OrderBy(m => m.Name);
            }

            FilteredMods.Clear();
            foreach (var mod in filtered)
            {
                FilteredMods.Add(mod);
            }
        }

        private void Confirm()
        {
            CloseDialog(SelectedMod);
        }

        private void Cancel()
        {
            CloseDialog(null);
        }
    }
}
