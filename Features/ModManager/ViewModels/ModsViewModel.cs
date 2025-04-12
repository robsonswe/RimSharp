using RimSharp.Core.Commands; // Keep for SelectModCommand maybe? Or move entirely?
using RimSharp.Core.Extensions;
using RimSharp.Features.ModManager.Services.Commands; // Likely not needed directly anymore
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections; // For potential SelectedItems binding
using System.Collections.Generic;
using System.ComponentModel; // For PropertyChangedEventArgs
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input; // For CommandManager

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModsViewModel : ViewModelBase, IDisposable
    {
        // --- Services ---
        private readonly IModDataService _dataService;
        private readonly IModFilterService _filterService;
        // private readonly IModCommandService _commandService; // Now likely used by children
        // private readonly IModListIOService _ioService; // Now used by ModActionsViewModel
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        // private readonly IModIncompatibilityService _incompatibilityService; // Used by ModActionsViewModel
        private readonly IPathService _pathService;


        // --- Child ViewModels ---
        public ModListViewModel ModListViewModel { get; private set; }
        public ModDetailsViewModel ModDetailsViewModel { get; private set; }
        public ModActionsViewModel ModActionsViewModel { get; private set; }

        public ICommand RequestRefreshCommand { get; }

        // --- State Properties (Managed by Parent) ---
        private bool _isLoading;
        private bool _hasUnsavedChanges;
        private ModItem _selectedMod;
        private IList _selectedItems; // Property to bind ListBox.SelectedItems

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    // Inform children about loading state change
                    if (ModListViewModel != null) ModListViewModel.IsParentLoading = value;
                    if (ModActionsViewModel != null) ModActionsViewModel.IsParentLoading = value;
                    RunOnUIThread(CommandManager.InvalidateRequerySuggested); // Update global commands
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    // Inform children
                    if (ModActionsViewModel != null) ModActionsViewModel.HasUnsavedChanges = value;
                    RunOnUIThread(CommandManager.InvalidateRequerySuggested); // Update Save command etc.
                }
            }
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (SetProperty(ref _selectedMod, value))
                {
                    // Update children that care about the single selected mod
                    // Just set the property; the child VM handles internal updates if needed
                    if (ModDetailsViewModel != null) ModDetailsViewModel.CurrentMod = value;
                    if (ModActionsViewModel != null) ModActionsViewModel.SelectedMod = value; // Setter triggers CanExecute changes
                }
            }
        }


        // Bound from the ListBox's SelectedItems property in the View
        public IList SelectedItems
        {
            get => _selectedItems;
            set
            {
                if (SetProperty(ref _selectedItems, value))
                {
                    // Inform ModActionsViewModel about the change in multiple selection
                    // Just set the property; the child VM handles internal updates if needed
                    if (ModActionsViewModel != null) ModActionsViewModel.SelectedItems = value; // Setter triggers CanExecute changes
                }
            }
        }



        // Constructor
        public ModsViewModel(
            // Inject all necessary services, even if passed down
            IModDataService dataService,
            IModFilterService filterService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService,
            IModService modService,
            IPathService pathService) // Assuming modService is needed somewhere, maybe dataService?
        {
            _dataService = dataService;
            _filterService = filterService;
            _modListManager = modListManager;
            _dialogService = dialogService;
            _pathService = pathService;

            RequestRefreshCommand = new AsyncRelayCommand(ExecuteRequestRefreshAsync, CanExecuteRequestRefresh);


            // Instantiate Child ViewModels, passing required dependencies
            ModListViewModel = new ModListViewModel(_filterService, _modListManager, commandService, _dialogService);
            ModDetailsViewModel = new ModDetailsViewModel(_dialogService);
            ModActionsViewModel = new ModActionsViewModel(
                _dataService, commandService, ioService, _modListManager, incompatibilityService, _dialogService, _pathService
                );

            // --- Event Wiring ---
            _modListManager.ListChanged += OnModListManagerChanged;

            // Wire events from children back to parent
            ModListViewModel.RequestSelectionChange += OnChildRequestSelectionChange; // Handle selection originating from list VM
            ModActionsViewModel.IsLoadingRequest += OnChildRequestLoading;
            ModActionsViewModel.RequestDataRefresh += OnChildRequestDataRefresh;
            ModActionsViewModel.HasUnsavedChangesRequest += OnChildRequestHasUnsavedChanges;


            // --- Initial Load ---
            // Ensure UI thread for async void pattern or use proper async initialization pattern
            ThreadHelper.EnsureUiThread(async () =>
            {
                await LoadDataAsync();
            });
        }

        // --- Event Handlers from Children ---

        private bool CanExecuteRequestRefresh()
        {
            // Can request refresh if not already loading
            return !IsLoading;
        }

        private async Task ExecuteRequestRefreshAsync(CancellationToken ct = default) // Add CT parameter
        {
            // Double check loading state inside execute in case of race conditions
            if (IsLoading)
            {
                Debug.WriteLine("[ModsViewModel] Refresh request ignored, already loading.");
                return;
            }

            Debug.WriteLine("[ModsViewModel] Executing refresh request...");
            await RefreshDataAsync(); // Call the actual worker method
            Debug.WriteLine("[ModsViewModel] Refresh execution complete.");
        }


        private void OnChildRequestSelectionChange(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModListViewModel.SelectedMod) && sender is ModListViewModel listVM)
            {
                // Update the parent's canonical SelectedMod property
                // This avoids circular updates if selection can be set from multiple places
                if (SelectedMod != listVM.SelectedMod)
                {
                    SelectedMod = listVM.SelectedMod;
                }
            }
        }

        private void OnChildRequestLoading(object sender, bool isLoading)
        {
            // A child VM (likely ModActionsViewModel) is performing a long operation
            IsLoading = isLoading;
        }

        private async void OnChildRequestDataRefresh(object sender, EventArgs e)
        {
            // Execute the command instead of calling RefreshDataAsync directly
            Debug.WriteLine("[ModsViewModel] Received request for data refresh from child. Executing RequestRefreshCommand.");
            if (RequestRefreshCommand.CanExecute(null))
            {
                // Use await Task.Run or similar if the command execution needs to be awaited
                // but AsyncRelayCommand handles the async nature internally.
                // We don't typically await command execution directly unless needed.
                RequestRefreshCommand.Execute(null);
            }
            else
            {
                Debug.WriteLine("[ModsViewModel] Cannot execute refresh command now (likely loading).");
            }
            // No need for await here as the command handles the async execution.
        }
        private void OnChildRequestHasUnsavedChanges(object sender, bool hasChanges)
        {
            // A child VM needs to update the global unsaved changes flag
            HasUnsavedChanges = hasChanges;
        }


        // --- Core Logic (Loading, Refresh, List Changes) ---

        public async Task RefreshDataAsync()
        {
            Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Starting refresh...");
            // Don't allow refresh if already loading to prevent re-entrancy issues
            if (IsLoading)
            {
                Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Refresh aborted, already loading.");
                return;
            }
            await LoadDataAsync(); // Reuse the main loading logic
            Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Refresh complete.");
        }

        private async Task LoadDataAsync()
        {
            if (IsLoading) return; // Prevent re-entrancy

            IsLoading = true; // This automatically updates children via property setter
            ProgressDialogViewModel progressDialog = null;

            try
            {
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog("Loading Mods", "Initializing...", false, true);
                });

                // Load data (as before)
                progressDialog.Message = "Loading mod data...";
                var allMods = await Task.Run(() => _dataService.LoadAllModsAsync()); // Ensure IO is off UI thread
                var activeIdsFromConfig = await Task.Run(() => _dataService.LoadActiveModIdsFromConfig()); // Ensure IO is off UI thread


                progressDialog.Message = "Initializing mod list...";
                // ModListManager initialization MUST happen before FilterService is updated.
                // ModListManager likely updates its internal lists here.
                _modListManager.Initialize(allMods, activeIdsFromConfig);

                // Now explicitly tell the FilterService to update its views based on the manager's state
                _filterService.UpdateCollections(
                    _modListManager.VirtualActiveMods,
                    _modListManager.AllInactiveMods
                );

                // Force UI update for counts in ModListViewModel
                ModListViewModel.RefreshCounts();

                progressDialog.Message = "Finalizing...";
                // Select initial item (logic remains the same)
                ModItem selectedModCandidate = _modListManager.VirtualActiveMods.FirstOrDefault(vm => vm.Mod.ModType == ModType.Core).Mod ?? // Find Core mod first
                                _modListManager.VirtualActiveMods.FirstOrDefault().Mod ?? // If no core, take first active
                                _modListManager.AllInactiveMods.FirstOrDefault();          // If no active, take first inactive

                // Set the parent's selected mod, which will propagate to children
                SelectedMod = selectedModCandidate;

                // Reset unsaved changes flag AFTER successful load
                HasUnsavedChanges = false; // ModActionsViewModel.HasUnsavedChanges gets updated via setter

                progressDialog.CompleteOperation("Mods loaded successfully");
                RunOnUIThread(CommandManager.InvalidateRequerySuggested); // Ensure commands update

            }
            catch (Exception ex)
            {
                progressDialog?.ForceClose(); // Ensure dialog closes on error
                Debug.WriteLine($"Error loading mods: {ex}");
                await RunOnUIThreadAsync(() => _dialogService.ShowError("Loading Error", $"Error loading mods: {ex.Message}"));
                HasUnsavedChanges = false; // Reset flag on error too
            }
            finally
            {
                IsLoading = false; // Reset loading state, triggers CanExecute updates etc.
            }
        }


        // Handles changes triggered by ModListManager (Activate, Deactivate, Sort, Clear, etc.)
        private void OnModListManagerChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("[ModsViewModel] Handling ModListManager ListChanged event (triggered on background thread potentially).");

            // --- IMPORTANT: Marshal UI-related updates back to the UI thread ---
            RunOnUIThread(() =>
            {
                Debug.WriteLine("[ModsViewModel] Marshalled to UI thread for updates.");

                // 1. Update Filter Service: Ensure the collections exposed by FilterService are synced
                //    with the ModListManager's current state.
                _filterService.UpdateCollections(
                    _modListManager.VirtualActiveMods,
                    _modListManager.AllInactiveMods
                );

                // 2. Refresh Counts in ModListViewModel: Explicitly trigger OnPropertyChanged for counts.
                //    (ViewModelBase.OnPropertyChanged already ensures UI thread for the event itself)
                ModListViewModel.RefreshCounts();

                // 3. Set HasUnsavedChanges Flag: Any change means we have unsaved state.
                //    This setter will propagate to ModActionsViewModel and trigger its command updates *on the UI thread*.
                HasUnsavedChanges = true;

                // 4. Invalidate Commands: Ensure commands re-evaluate CanExecute.
                //    CommandManager.InvalidateRequerySuggested is UI thread safe.
                //    Setting HasUnsavedChanges *should* trigger the necessary updates via the chain reaction,
                //    but calling this explicitly ensures global commands not tied directly to HasUnsavedChanges also update.
                CommandManager.InvalidateRequerySuggested();

                Debug.WriteLine("[ModsViewModel] Finished handling ListChanged event on UI thread.");
            });
        }



        // --- IDisposable Implementation ---
        private bool _disposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events
                    if (_modListManager != null) _modListManager.ListChanged -= OnModListManagerChanged;
                    if (ModListViewModel != null) ModListViewModel.RequestSelectionChange -= OnChildRequestSelectionChange;
                    if (ModActionsViewModel != null)
                    {
                        ModActionsViewModel.IsLoadingRequest -= OnChildRequestLoading;
                        ModActionsViewModel.RequestDataRefresh -= OnChildRequestDataRefresh;
                        ModActionsViewModel.HasUnsavedChangesRequest -= OnChildRequestHasUnsavedChanges;
                    }


                    // Dispose child ViewModels if they implement IDisposable
                    (ModListViewModel as IDisposable)?.Dispose();
                    (ModDetailsViewModel as IDisposable)?.Dispose();
                    (ModActionsViewModel as IDisposable)?.Dispose();

                    Debug.WriteLine("[ModsViewModel] Disposed managed resources.");
                }

                // Dispose unmanaged resources here if any

                _disposed = true;
            }
        }

        ~ModsViewModel()
        {
            Dispose(false);
            Debug.WriteLine("[ModsViewModel] Finalizer called.");
        }

        // --- Helper Methods --- (Copied from ViewModelBase, ensure they are accessible)
        // protected void RunOnUIThread(Action action) => ThreadHelper.EnsureUiThread(action);
        // protected async Task RunOnUIThreadAsync(Action action) => await Task.Run(() => RunOnUIThread(action));
    }
}