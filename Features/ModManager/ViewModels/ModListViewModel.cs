using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Threading.Tasks;

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModListViewModel : ViewModelBase
    {
        private readonly IModFilterService _filterService;
        private readonly IModListManager _modListManager;
        private readonly IModCommandService _commandService;
        private readonly IDialogService _dialogService;
        private ModItem _selectedMod;
        private bool _isParentLoading;

        public ObservableCollection<ModItem> ActiveMods => _filterService.ActiveMods;
        public ObservableCollection<ModItem> InactiveMods => _filterService.InactiveMods;

        public int TotalActiveMods => ActiveMods.Count;
        public int TotalInactiveMods => InactiveMods.Count;

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set => SetProperty(ref _selectedMod, value);
        }

        public string ActiveSearchText
        {
            get => _filterService.ActiveSearchText;
            set
            {
                if (_filterService.ActiveSearchText != value)
                {
                    _filterService.ApplyActiveFilter(value);
                    OnPropertyChanged(nameof(ActiveSearchText));
                    OnPropertyChanged(nameof(TotalActiveMods));
                }
            }
        }


        public string InactiveSearchText
        {
            get => _filterService.InactiveSearchText;
            set
            {
                if (_filterService.InactiveSearchText != value)
                {
                    _filterService.ApplyInactiveFilter(value);
                    OnPropertyChanged(nameof(InactiveSearchText));
                    OnPropertyChanged(nameof(TotalInactiveMods));
                }
            }
        }

        public bool IsParentLoading
        {
            get => _isParentLoading;
            set => SetProperty(ref _isParentLoading, value);
        }

        public ICommand ActivateModCommand { get; }
        public ICommand DeactivateModCommand { get; }
        public ICommand DropModCommand { get; }
        public ICommand FilterInactiveCommand { get; }
        public ICommand FilterActiveCommand { get; }

        public event PropertyChangedEventHandler RequestSelectionChange;

        public ModListViewModel(IModFilterService filterService, IModListManager modListManager, IModCommandService commandService, IDialogService dialogService)
        {
            _filterService = filterService;
            _modListManager = modListManager;
            _commandService = commandService;
            _dialogService = dialogService;

            _filterService.ActiveMods.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TotalActiveMods));
            _filterService.InactiveMods.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TotalInactiveMods));

            // Use property observation syntax from ViewModelBase
            ActivateModCommand = CreateCommand<ModItem>(
                mod => { _modListManager.ActivateMod(mod); },
                mod => mod != null,
                new[] { nameof(IsParentLoading) }); // Corrected: Use array syntax

            DeactivateModCommand = CreateCommand<ModItem>(
                mod => { _modListManager.DeactivateMod(mod); },
                mod => mod != null && mod.ModType != ModType.Core,
                new[] { nameof(IsParentLoading) }); // Corrected: Use array syntax

            DropModCommand = CreateAsyncCommand<DropModArgs>(
                args => _commandService.HandleDropCommand(args),
                args => true,
                new[] { nameof(IsParentLoading) }); // Corrected: Use array syntax

            FilterInactiveCommand = CreateCommand(
                () => dialogService.ShowInformation("Not Implemented", "Filter Inactive Mods - Not Yet Implemented"),
                () => true,
                new[] { nameof(IsParentLoading) }); // Corrected: Use array syntax

            FilterActiveCommand = CreateCommand(
                () => dialogService.ShowInformation("Not Implemented", "Filter Active Mods - Not Yet Implemented"),
                () => true,
                new[] { nameof(IsParentLoading) }); // Corrected: Use array syntax
        }

        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));
        }

        public void UpdateSelectedMod(ModItem mod)
        {
            SelectedMod = mod;
        }

        public void HandleListBoxSelectionChanged(ModItem newlySelectedItem)
        {
            if (SetProperty(ref _selectedMod, newlySelectedItem, nameof(SelectedMod)))
            {
                RequestSelectionChange?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMod)));
            }
        }
    }
}
