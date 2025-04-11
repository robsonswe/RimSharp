using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Services.Commands; // For DropModArgs
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModListViewModel : ViewModelBase
    {
        private readonly IModFilterService _filterService;
        private readonly IModListManager _modListManager;
        private readonly IModCommandService _commandService; // For Drop
        private ModItem _selectedMod;
        private bool _isParentLoading;

        public ObservableCollection<ModItem> ActiveMods => _filterService.ActiveMods;
        public ObservableCollection<ModItem> InactiveMods => _filterService.InactiveMods;

        public int TotalActiveMods => ActiveMods.Count;
        public int TotalInactiveMods => InactiveMods.Count;

        // This VM might still need to know the selected mod for context,
        // but the main ModsViewModel will likely own the canonical SelectedMod instance.
        public ModItem SelectedMod
        {
            get => _selectedMod;
            set => SetProperty(ref _selectedMod, value); // Can be set by parent
        }

        public string ActiveSearchText
        {
            get => _filterService.ActiveSearchText;
            set
            {
                if (_filterService.ActiveSearchText == value) return;
                _filterService.ApplyActiveFilter(value);
                OnPropertyChanged(nameof(ActiveSearchText));
                OnPropertyChanged(nameof(TotalActiveMods)); // Filter changes counts
            }
        }

        public string InactiveSearchText
        {
            get => _filterService.InactiveSearchText;
            set
            {
                if (_filterService.InactiveSearchText == value) return;
                _filterService.ApplyInactiveFilter(value);
                OnPropertyChanged(nameof(InactiveSearchText));
                OnPropertyChanged(nameof(TotalInactiveMods)); // Filter changes counts
            }
        }

        // Keep track of parent's loading state for CanExecute checks
        public bool IsParentLoading
        {
            get => _isParentLoading;
            set
            {
                if (SetProperty(ref _isParentLoading, value))
                {
                    // Update commands that depend on loading state
                    (ActivateModCommand as RelayCommand<ModItem>)?.RaiseCanExecuteChanged();
                    (DeactivateModCommand as RelayCommand<ModItem>)?.RaiseCanExecuteChanged();
                    (DropModCommand as RelayCommand<DropModArgs>)?.RaiseCanExecuteChanged();
                    (FilterInactiveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (FilterActiveCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // --- Commands specific to list interaction ---
        public ICommand ActivateModCommand { get; private set; }
        public ICommand DeactivateModCommand { get; private set; }
        public ICommand DropModCommand { get; private set; }
        public ICommand FilterInactiveCommand { get; private set; } // Placeholder
        public ICommand FilterActiveCommand { get; private set; } // Placeholder

        // Event to notify parent about selection change initiated from the list view
        public event PropertyChangedEventHandler RequestSelectionChange;


        public ModListViewModel(IModFilterService filterService, IModListManager modListManager, IModCommandService commandService, IDialogService dialogService)
        {
            _filterService = filterService;
            _modListManager = modListManager;
            _commandService = commandService; // Needed for DropModCommand

            // Listen to filter service changes if needed (e.g., if external forces update lists)
            _filterService.ActiveMods.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TotalActiveMods));
            _filterService.InactiveMods.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TotalInactiveMods));


            ActivateModCommand = new RelayCommand<ModItem>(
                mod => { _modListManager.ActivateMod(mod); /* Parent handles HasUnsavedChanges */ },
                mod => mod != null && !IsParentLoading);

            DeactivateModCommand = new RelayCommand<ModItem>(
                mod => { _modListManager.DeactivateMod(mod); /* Parent handles HasUnsavedChanges */ },
                mod => mod != null && !mod.IsCore && !IsParentLoading);

            DropModCommand = new RelayCommand<DropModArgs>(
                async args => await _commandService.HandleDropCommand(args),
                args => !IsParentLoading);

            // Placeholder commands - Link to DialogService if needed
            FilterInactiveCommand = new RelayCommand(_ => dialogService.ShowInformation("Not Implemented", "Filter Inactive Mods - Not Yet Implemented"), _ => !IsParentLoading);
            FilterActiveCommand = new RelayCommand(_ => dialogService.ShowInformation("Not Implemented", "Filter Active Mods - Not Yet Implemented"), _ => !IsParentLoading);

        }

         // Method for parent to update counts if filter service doesn't raise changes reliably
        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));
        }

        // Method to update the selected mod from the parent
        public void UpdateSelectedMod(ModItem mod)
        {
            SelectedMod = mod;
        }

        // Method called by the View when selection changes in the ListBox
        // We raise an event so the parent ModsViewModel can handle the canonical selection state
        public void HandleListBoxSelectionChanged(ModItem newlySelectedItem)
        {
             // Update local property for binding if needed, but notify parent primarily
            SetProperty(ref _selectedMod, newlySelectedItem, nameof(SelectedMod));
            RequestSelectionChange?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMod)));
        }
    }
}
