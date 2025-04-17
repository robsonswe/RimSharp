using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Threading.Tasks;
using RimSharp.Features.ModManager.Dialogs.Filter;
using System.Linq;


namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModListViewModel : ViewModelBase
    {
        private readonly IModFilterService _filterService;
        private readonly IModListManager _modListManager;
        private readonly IModCommandService _commandService;
        private readonly IDialogService _dialogService;
        private ModItem _selectedMod;
        public bool IsActiveFilterApplied => _filterService.ActiveFilterCriteria.IsActive();
        public bool IsInactiveFilterApplied => _filterService.InactiveFilterCriteria.IsActive();

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
            get => _filterService.ActiveSearchText; // Read directly from service's current state
            set
            {
                // Use the simple ApplyActiveFilter which updates the criteria internally
                if (_filterService.ActiveSearchText != value)
                {
                    _filterService.ApplyActiveFilter(value); // This now updates criteria AND filters
                    OnPropertyChanged(nameof(ActiveSearchText));
                    OnPropertyChanged(nameof(TotalActiveMods)); // Total mods might change
                }
            }
        }

        public string InactiveSearchText
        {
            get => _filterService.InactiveSearchText; // Read directly from service's current state
            set
            {
                // Use the simple ApplyInactiveFilter which updates the criteria internally
                if (_filterService.InactiveSearchText != value)
                {
                    _filterService.ApplyInactiveFilter(value); // This now updates criteria AND filters
                    OnPropertyChanged(nameof(InactiveSearchText));
                    OnPropertyChanged(nameof(TotalInactiveMods)); // Total mods might change
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

  _filterService.ActiveMods.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(TotalActiveMods));
                OnPropertyChanged(nameof(IsActiveFilterApplied)); // Update button state/indicator
            };
            _filterService.InactiveMods.CollectionChanged += (s, e) => {
                 OnPropertyChanged(nameof(TotalInactiveMods));
                 OnPropertyChanged(nameof(IsInactiveFilterApplied)); // Update button state/indicator
            };

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
                ShowFilterDialogForInactive,
                () => !IsParentLoading, // Can execute when not loading
                nameof(IsParentLoading)); // Observe IsParentLoading

            FilterActiveCommand = CreateCommand(
                ShowFilterDialogForActive,
                () => !IsParentLoading, // Can execute when not loading
                nameof(IsParentLoading)); // Observe IsParentLoading
        }

        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(TotalInactiveMods));
            OnPropertyChanged(nameof(IsActiveFilterApplied));
            OnPropertyChanged(nameof(IsInactiveFilterApplied));
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
        // --- Filter Dialog Logic ---
        private void ShowFilterDialogForActive()
        {
            ShowFilterDialog(isForActiveList: true);
        }

        private void ShowFilterDialogForInactive()
        {
            ShowFilterDialog(isForActiveList: false);
        }

        private void ShowFilterDialog(bool isForActiveList)
        {
            // Get current criteria and available filter options from the service
            var currentCriteria = isForActiveList ? _filterService.ActiveFilterCriteria : _filterService.InactiveFilterCriteria;
            var availableVersions = _filterService.AllAvailableSupportedVersions ?? Enumerable.Empty<string>();
            var availableTags = _filterService.AllAvailableTags ?? Enumerable.Empty<string>();
            var availableAuthors = _filterService.AllAvailableAuthors ?? Enumerable.Empty<string>();


            var viewModel = new ModFilterDialogViewModel(currentCriteria, availableVersions, availableTags, availableAuthors);

            var result = _dialogService.ShowModFilterDialog(viewModel);

            switch (result)
            {
                case ModFilterDialogResult.Apply:
                    if (isForActiveList)
                    {
                        _filterService.ApplyActiveFilterCriteria(viewModel.CurrentCriteria);
                        // Sync SearchText property only if it was potentially changed via the dialog's main search box
                        // This avoids overwriting manual TextBox input if Apply was clicked without changing that box
                         if (ActiveSearchText != viewModel.CurrentCriteria.SearchText)
                         {
                              OnPropertyChanged(nameof(ActiveSearchText));
                         }
                         OnPropertyChanged(nameof(TotalActiveMods));
                         OnPropertyChanged(nameof(IsActiveFilterApplied)); // Update filter status
                    }
                    else
                    {
                        _filterService.ApplyInactiveFilterCriteria(viewModel.CurrentCriteria);
                         if (InactiveSearchText != viewModel.CurrentCriteria.SearchText)
                         {
                              OnPropertyChanged(nameof(InactiveSearchText));
                         }
                        OnPropertyChanged(nameof(TotalInactiveMods));
                        OnPropertyChanged(nameof(IsInactiveFilterApplied)); // Update filter status
                    }
                    break;

                case ModFilterDialogResult.Clear:
                    if (isForActiveList)
                    {
                        _filterService.ClearActiveFilters();
                        OnPropertyChanged(nameof(ActiveSearchText)); // Search text is cleared
                        OnPropertyChanged(nameof(TotalActiveMods));
                        OnPropertyChanged(nameof(IsActiveFilterApplied)); // Update filter status
                    }
                    else
                    {
                        _filterService.ClearInactiveFilters();
                        OnPropertyChanged(nameof(InactiveSearchText)); // Search text is cleared
                        OnPropertyChanged(nameof(TotalInactiveMods));
                        OnPropertyChanged(nameof(IsInactiveFilterApplied)); // Update filter status
                    }
                    break;

                case ModFilterDialogResult.Cancel:
                    // No changes needed
                    break;
            }
        }


    }
}
