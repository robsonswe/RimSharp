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
    public enum ModSelectorHintKind
    {
        None,
        Dependency,
        Custom,
        Overridden,
        Disabled,
        Inherited
    }

    public sealed class ModSelectorHint
    {
        public string Text { get; }
        public ModSelectorHintKind Kind { get; }

        public ModSelectorHint(string text, ModSelectorHintKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    public sealed class ModSelectorRowViewModel
    {
        public ModItem Mod { get; }
        public string DisplayName => Mod.DisplayName;
        public string PackageId => Mod.PackageId;
        public string SteamId => Mod.SteamId;
        public string Authors => Mod.Authors;
        public IEnumerable<string> Versions => Mod.SupportedVersionStrings.TakeLast(4);
        public string? HintText { get; }
        public ModSelectorHintKind HintKind { get; }
        public bool HasHint => !string.IsNullOrWhiteSpace(HintText);
        public bool IsDependencyHint => HintKind == ModSelectorHintKind.Dependency;
        public bool IsCustomHint => HintKind == ModSelectorHintKind.Custom;
        public bool IsOverriddenHint => HintKind == ModSelectorHintKind.Overridden;
        public bool IsDisabledHint => HintKind == ModSelectorHintKind.Disabled;
        public bool IsInheritedHint => HintKind == ModSelectorHintKind.Inherited;

        public ModSelectorRowViewModel(ModItem mod, ModSelectorHint? hint)
        {
            Mod = mod;
            HintText = hint?.Text;
            HintKind = hint?.Kind ?? ModSelectorHintKind.None;
        }
    }

    public class ModSelectorDialogViewModel : DialogViewModelBase<ModItem?>
    {
        private readonly List<ModItem> _allMods;
        private readonly IReadOnlyDictionary<string, ModSelectorHint>? _hints;
        private string _searchText = string.Empty;
        private ModItem? _selectedMod;
        private ModSelectorRowViewModel? _selectedRow;
        private const int FuzzySearchThreshold = 75;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        public ModItem? SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (SetProperty(ref _selectedMod, value))
                {
                    var matchingRow = FilteredRows.FirstOrDefault(r => ReferenceEquals(r.Mod, value));
                    if (!ReferenceEquals(_selectedRow, matchingRow))
                    {
                        _selectedRow = matchingRow;
                        OnPropertyChanged(nameof(SelectedRow));
                    }
                }
            }
        }

        public ModSelectorRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                    SelectedMod = value?.Mod;
            }
        }

        public ObservableCollection<ModItem> FilteredMods { get; } = new();
        public ObservableCollection<ModSelectorRowViewModel> FilteredRows { get; } = new();

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public ModSelectorDialogViewModel(
            IEnumerable<ModItem> allMods,
            IReadOnlyDictionary<string, ModSelectorHint>? hints = null)
            : base("Select Mod")
        {
            _allMods = allMods?.ToList() ?? new List<ModItem>();
            _hints = hints;

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
                    (m.DisplayName?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (m.PackageId?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    Fuzz.TokenSetRatio(m.Name?.ToLowerInvariant() ?? "", searchLower) >= FuzzySearchThreshold
                ).OrderBy(m => m.Name);
            }

            FilteredMods.Clear();
            FilteredRows.Clear();
            foreach (var mod in filtered)
            {
                FilteredMods.Add(mod);
                ModSelectorHint? hint = null;
                if (_hints != null && !string.IsNullOrWhiteSpace(mod.PackageId))
                    _hints.TryGetValue(mod.PackageId, out hint);
                FilteredRows.Add(new ModSelectorRowViewModel(mod, hint));
            }
        }

        private void Confirm() => CloseDialog(SelectedMod);
        private void Cancel() => CloseDialog(null);
    }
}
