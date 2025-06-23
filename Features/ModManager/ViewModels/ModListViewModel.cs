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
using System.Threading; // Added for CancellationTokenSource

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModListViewModel : ViewModelBase
    {
        private readonly IModFilterService _filterService;
        private readonly IModListManager _modListManager;
        private readonly IModCommandService _commandService;
        private readonly IDialogService _dialogService;
        private ModItem _selectedMod;

        // --- Backing fields and cancellation tokens for debounced search ---
        private string _activeSearchText;
        private string _inactiveSearchText;
        private CancellationTokenSource _activeSearchCts = new CancellationTokenSource();
        private CancellationTokenSource _inactiveSearchCts = new CancellationTokenSource();
        // ---

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
            get => _activeSearchText;
            set
            {
                if (SetProperty(ref _activeSearchText, value))
                {
                    DebounceFilter(isForActiveList: true);
                }
            }
        }

        public string InactiveSearchText
        {
            get => _inactiveSearchText;
            set
            {
                if (SetProperty(ref _inactiveSearchText, value))
                {
                    DebounceFilter(isForActiveList: false);
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

            // Initialize local search text from the service's state on creation
            _activeSearchText = _filterService.ActiveSearchText;
            _inactiveSearchText = _filterService.InactiveSearchText;

            // Use named methods for event handlers to allow for proper unsubscription on Dispose
            _filterService.ActiveMods.CollectionChanged += OnActiveModsChanged;
            _filterService.InactiveMods.CollectionChanged += OnInactiveModsChanged;

            // Use property observation syntax from ViewModelBase
            ActivateModCommand = CreateCommand<ModItem>(
                mod => { _modListManager.ActivateMod(mod); },
                mod => mod != null,
                nameof(IsParentLoading));

            DeactivateModCommand = CreateCommand<ModItem>(
                mod => { _modListManager.DeactivateMod(mod); },
                mod => mod != null && mod.ModType != ModType.Core,
                nameof(IsParentLoading));

            DropModCommand = CreateAsyncCommand<DropModArgs>(
                args => _commandService.HandleDropCommand(args),
                args => true,
                nameof(IsParentLoading));

            FilterInactiveCommand = CreateCommand(
                ShowFilterDialogForInactive,
                () => !IsParentLoading, // Can execute when not loading
                nameof(IsParentLoading)); // Observe IsParentLoading

            FilterActiveCommand = CreateCommand(
                ShowFilterDialogForActive,
                () => !IsParentLoading, // Can execute when not loading
                nameof(IsParentLoading)); // Observe IsParentLoading
        }

        /// <summary>
        /// Asynchronously applies the filter after a short delay, canceling previous requests.
        /// </summary>
        private async void DebounceFilter(bool isForActiveList)
        {
            try
            {
                if (isForActiveList)
                {
                    _activeSearchCts.Cancel();
                    _activeSearchCts = new CancellationTokenSource();
                    await Task.Delay(300, _activeSearchCts.Token);

                    // Apply the filter using the current text from the backing field
                    _filterService.ApplyActiveFilter(_activeSearchText);
                }
                else
                {
                    _inactiveSearchCts.Cancel();
                    _inactiveSearchCts = new CancellationTokenSource();
                    await Task.Delay(300, _inactiveSearchCts.Token);

                    _filterService.ApplyInactiveFilter(_inactiveSearchText);
                }
            }
            catch (TaskCanceledException)
            {
                // This is expected if the user types quickly. Ignore.
                Debug.WriteLine("[ModListViewModel] Search debounce cancelled.");
            }
        }

        #region Event Handlers
        private void OnActiveModsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(TotalActiveMods));
            OnPropertyChanged(nameof(IsActiveFilterApplied));
        }

        private void OnInactiveModsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(TotalInactiveMods));
            OnPropertyChanged(nameof(IsInactiveFilterApplied));
        }
        #endregion

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
                        // Sync our local backing field and notify the UI without re-triggering the debounce.
                        if (_activeSearchText != _filterService.ActiveSearchText)
                        {
                            _activeSearchText = _filterService.ActiveSearchText;
                            OnPropertyChanged(nameof(ActiveSearchText));
                        }
                    }
                    else
                    {
                        _filterService.ApplyInactiveFilterCriteria(viewModel.CurrentCriteria);
                        if (_inactiveSearchText != _filterService.InactiveSearchText)
                        {
                            _inactiveSearchText = _filterService.InactiveSearchText;
                            OnPropertyChanged(nameof(InactiveSearchText));
                        }
                    }
                    break;

                case ModFilterDialogResult.Clear:
                    if (isForActiveList)
                    {
                        _filterService.ClearActiveFilters();
                        // Sync our local backing field and notify the UI.
                        if (_activeSearchText != _filterService.ActiveSearchText)
                        {
                            _activeSearchText = _filterService.ActiveSearchText;
                            OnPropertyChanged(nameof(ActiveSearchText));
                        }
                    }
                    else
                    {
                        _filterService.ClearInactiveFilters();
                        if (_inactiveSearchText != _filterService.InactiveSearchText)
                        {
                            _inactiveSearchText = _filterService.InactiveSearchText;
                            OnPropertyChanged(nameof(InactiveSearchText));
                        }
                    }
                    break;

                case ModFilterDialogResult.Cancel:
                    // No changes needed
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // --- Clean up managed resources ---
                    
                    // Unsubscribe from events to prevent memory leaks
                    if (_filterService != null)
                    {
                        _filterService.ActiveMods.CollectionChanged -= OnActiveModsChanged;
                        _filterService.InactiveMods.CollectionChanged -= OnInactiveModsChanged;
                    }
                    
                    // Cancel any pending debounced searches and dispose tokens
                    _activeSearchCts?.Cancel();
                    _activeSearchCts?.Dispose();
                    _inactiveSearchCts?.Cancel();
                    _inactiveSearchCts?.Dispose();
                }
                
                base.Dispose(disposing);
            }
        }
    }
}