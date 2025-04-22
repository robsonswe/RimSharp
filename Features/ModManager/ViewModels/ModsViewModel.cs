
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
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
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IPathService _pathService;
        private readonly IModService _modService;
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor;

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
        public bool HasAnyActiveModIssues => _modListManager?.HasAnyActiveModIssues ?? false;

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                Debug.WriteLine($"[ModsViewModel] IsLoading SETTER: Current = {_isLoading}, New = {value}");
                if (SetProperty(ref _isLoading, value))
                {
                    Debug.WriteLine($"[ModsViewModel] IsLoading Changed to {value}. Notifying children...");
                    if (ModListViewModel != null) ModListViewModel.IsParentLoading = value;
                    if (ModActionsViewModel != null)
                    {
                        Debug.WriteLine($"[ModsViewModel] Setting ModActionsViewModel.IsParentLoading = {value}");
                        ModActionsViewModel.IsParentLoading = value;
                    }
                    RunOnUIThread(CommandManager.InvalidateRequerySuggested);
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                // Use base SetProperty
                if (SetProperty(ref _hasUnsavedChanges, value))
                {
                    // Inform children
                    if (ModActionsViewModel != null) ModActionsViewModel.HasUnsavedChanges = value;
                    // Command observation handles CanExecute updates for commands observing HasUnsavedChanges
                    RunOnUIThread(CommandManager.InvalidateRequerySuggested);
                }
            }
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set
            {
                // Use base SetProperty
                if (SetProperty(ref _selectedMod, value))
                {
                    // Update children that care about the single selected mod
                    if (ModDetailsViewModel != null) ModDetailsViewModel.CurrentMod = value;
                    if (ModActionsViewModel != null) ModActionsViewModel.SelectedMod = value; // Setter triggers CanExecute changes in child
                }
            }
        }


        // Bound from the ListBox's SelectedItems property in the View
        public IList SelectedItems
        {
            get => _selectedItems;
            set
            {
                // Use base SetProperty
                if (SetProperty(ref _selectedItems, value))
                {
                    // Inform ModActionsViewModel about the change in multiple selection
                    if (ModActionsViewModel != null) ModActionsViewModel.SelectedItems = value; // Setter triggers CanExecute changes in child
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
            IPathService pathService,
            IModReplacementService replacementService,
            IDownloadQueueService downloadQueueService,
            ISteamApiClient steamApiClient,
            IApplicationNavigationService navigationService,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor)
        {
            _dataService = dataService;
            _filterService = filterService;
            _modListManager = modListManager;
            _dialogService = dialogService;
            _pathService = pathService;
            _modService = modService;
            _steamWorkshopQueueProcessor = steamWorkshopQueueProcessor;

            // Initialize command using base helper, observing IsLoading
            // Use CreateCancellableAsyncCommand as ExecuteRequestRefreshAsync accepts a CancellationToken
            RequestRefreshCommand = CreateCancellableAsyncCommand(ExecuteRequestRefreshAsync, CanExecuteRequestRefresh, nameof(IsLoading));

            // Instantiate Child ViewModels, passing required dependencies
            ModListViewModel = new ModListViewModel(_filterService, _modListManager, commandService, _dialogService);
            ModDetailsViewModel = new ModDetailsViewModel(_dialogService);
            ModActionsViewModel = new ModActionsViewModel(
                _dataService, commandService, ioService, _modListManager, incompatibilityService, _dialogService, _pathService, _modService,
                        replacementService, downloadQueueService, steamApiClient, navigationService, steamWorkshopQueueProcessor
                        );

            // --- Event Wiring ---
            _modListManager.ListChanged += OnModListManagerChanged;

            // Wire events from children back to parent
            ModListViewModel.RequestSelectionChange += OnChildRequestSelectionChange;
            ModActionsViewModel.IsLoadingRequest += OnChildRequestLoading;
            ModActionsViewModel.RequestDataRefresh += OnChildRequestDataRefresh;
            ModActionsViewModel.HasUnsavedChangesRequest += OnChildRequestHasUnsavedChanges;


            // --- Initial Load ---
            // Ensure UI thread for async void pattern or use proper async initialization pattern
            RunOnUIThread(async () => await LoadDataAsync());
        }

        // --- Event Handlers from Children ---

        private bool CanExecuteRequestRefresh()
        {
            // Can request refresh if not already loading (dependency observed by command)
            return !IsLoading;
        }

        private async Task ExecuteRequestRefreshAsync(CancellationToken ct) // Add CT parameter
        {
            // CanExecute check done by command framework.
            // Double check loading state inside execute in case of race conditions is good practice though.
            if (IsLoading)
            {
                Debug.WriteLine("[ModsViewModel] Refresh request ignored, already loading.");
                return;
            }

            Debug.WriteLine("[ModsViewModel] Executing refresh request...");

            // ---> ADD PATH REFRESH AND CHILD VM UPDATE HERE <---
            try
            {
                // It's generally safe to call these service methods, but run on UI thread if there's any doubt
                // about thread safety within the services or if they might trigger UI updates directly.
                RunOnUIThread(() =>
                {
                    Debug.WriteLine("[ModsViewModel] Refreshing PathService and ModActionsViewModel path validity...");
                    _pathService.RefreshPaths(); // Tell PathService to re-read config/recache its values
                    ModActionsViewModel?.RefreshPathValidity(); // Tell Actions VM to update its HasValidPaths property based on new service state
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModsViewModel] Error during path refresh: {ex.Message}");
                // Decide if you want to stop the refresh or continue despite path errors
                // Maybe show an error message?
                // await RunOnUIThreadAsync(() => _dialogService.ShowError("Path Refresh Error", $"Failed to refresh paths: {ex.Message}"));
                // return; // Optional: stop the refresh if paths are critical and failed to update
            }


            await RefreshDataAsync(ct); // Pass cancellation token down (This also sets IsLoading)
            Debug.WriteLine("[ModsViewModel] Refresh execution complete.");
        }



        private void OnChildRequestSelectionChange(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModListViewModel.SelectedMod) && sender is ModListViewModel listVM)
            {
                // Check prevents infinite loop if parent setting triggered the child change
                if (SelectedMod != listVM.SelectedMod)
                {
                    SelectedMod = listVM.SelectedMod;
                }
            }
            // Handle multi-selection change if needed (less common)
            // else if (e.PropertyName == nameof(ModListViewModel.SelectedItems) && sender is ModListViewModel listVM) { ... }
        }

        private void OnChildRequestLoading(object sender, bool isLoading)
        {
            // A child VM (likely ModActionsViewModel) is performing a long operation
            IsLoading = isLoading; // Setter handles notifications and command updates via observation
        }

        private void OnChildRequestDataRefresh(object sender, EventArgs e)
        {
            Debug.WriteLine("[ModsViewModel] Received request for data refresh from child. Executing RequestRefreshCommand.");
            if (RequestRefreshCommand.CanExecute(null))
            {
                // Execute the command. AsyncRelayCommand handles the async execution.
                // We don't typically await command execution directly unless needed.
                RequestRefreshCommand.Execute(null); // Pass null/default CancellationToken if not provided by requester
            }
            else
            {
                Debug.WriteLine("[ModsViewModel] Cannot execute refresh command now (likely loading).");
            }
        }
        private void OnChildRequestHasUnsavedChanges(object sender, bool hasChanges)
        {
            // A child VM (ModActionsViewModel) has completed a save action
            // Only expecting 'false' here now.
            Debug.WriteLine($"[ModsViewModel] Received HasUnsavedChangesRequest: {hasChanges}. Setting parent state.");
            HasUnsavedChanges = hasChanges; // Setter handles notifications and command updates via observation
        }


        // --- Core Logic (Loading, Refresh, List Changes) ---

        public async Task RefreshDataAsync(CancellationToken ct = default) // Accept cancellation token
        {
            Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Starting refresh...");
            if (IsLoading)
            {
                Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Refresh aborted, already loading.");
                return;
            }
            await LoadDataAsync(ct); // Reuse the main loading logic, passing token
            Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Refresh complete.");
        }

        private async Task LoadDataAsync(CancellationToken ct = default)
        {
            if (IsLoading) return; // Prevent re-entrancy

            RunOnUIThread(() =>
            {
                Debug.WriteLine("[ModsViewModel] Initial path refresh during LoadDataAsync...");
                _pathService.RefreshPaths();
                ModActionsViewModel?.RefreshPathValidity();
            });

            IsLoading = true; // Setter updates children and commands
            ProgressDialogViewModel progressDialog = null;

            try
            {
                // Check for cancellation before showing dialog
                ct.ThrowIfCancellationRequested();

                await RunOnUIThreadAsync(() =>
                {
                    // Pass a new CTS linked to the incoming token for the dialog
                    var dialogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    progressDialog = _dialogService.ShowProgressDialog("Loading Mods", "Initializing...", false, true, dialogCts, false);
                    // Handle cancellation from dialog if needed: progressDialog.Cancelled += (s,e) => { /* maybe log */ };
                });

                // Check for cancellation after showing dialog
                ct.ThrowIfCancellationRequested();


                // Load data (as before), passing token if service methods support it
                progressDialog.Message = "Loading mod data...";
                // Assuming services support cancellation or Task.Run respects it
                var allMods = await Task.Run(async () => await _dataService.LoadAllModsAsync(), ct);
                ct.ThrowIfCancellationRequested();
                var activeIdsFromConfig = await Task.Run(() => _dataService.LoadActiveModIdsFromConfig(), ct);
                ct.ThrowIfCancellationRequested();


                progressDialog.Message = "Initializing mod list...";
                // ModListManager initialization MUST happen before FilterService is updated.
                // Assuming Initialize is synchronous or handles cancellation internally if async
                _modListManager.Initialize(allMods, activeIdsFromConfig);
                ct.ThrowIfCancellationRequested();

                // Now explicitly tell the FilterService to update its views based on the manager's state
                _filterService.UpdateCollections(
                    _modListManager.VirtualActiveMods,
                    _modListManager.AllInactiveMods
                );
                ct.ThrowIfCancellationRequested();

                // Force UI update for counts in ModListViewModel
                ModListViewModel.RefreshCounts();

                progressDialog.Message = "Finalizing...";
                ModItem selectedModCandidate = _modListManager.VirtualActiveMods.FirstOrDefault(vm => vm.Mod.ModType == ModType.Core).Mod ??
                                _modListManager.VirtualActiveMods.FirstOrDefault().Mod ??
                                _modListManager.AllInactiveMods.FirstOrDefault();

                // Set the parent's selected mod, which will propagate to children
                SelectedMod = selectedModCandidate;

                // Reset unsaved changes flag AFTER successful load
                HasUnsavedChanges = false; // Setter updates ModActionsViewModel and commands

                progressDialog.CompleteOperation("Mods loaded successfully");
                // CommandManager.InvalidateRequerySuggested might still be useful for global commands
                RunOnUIThread(CommandManager.InvalidateRequerySuggested);

            }
            catch (OperationCanceledException)
            {
                progressDialog?.ForceClose(); // Close dialog on cancellation
                Debug.WriteLine("[ModsViewModel] LoadDataAsync cancelled.");
                // Optionally show a cancelled message, or just reset state
                await RunOnUIThreadAsync(() => _dialogService.ShowWarning("Load Cancelled", "Mod loading was cancelled."));
                HasUnsavedChanges = false; // Reset flag
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
                Debug.WriteLine($"[ModsViewModel] LoadDataAsync FINALLY block entered. Setting IsLoading = false.");
                IsLoading = false; // Reset loading state, setter handles updates
                Debug.WriteLine($"[ModsViewModel] LoadDataAsync FINALLY block finished. IsLoading should be false.");
            }
        }


        // Handles changes triggered by ModListManager (Activate, Deactivate, Sort, Clear, etc.)
        private void OnModListManagerChanged(object sender, EventArgs e)
        {
            Debug.WriteLine("[ModsViewModel] Handling ModListManager ListChanged event.");

            RunOnUIThread(() =>
            {
                Debug.WriteLine("[ModsViewModel] Marshalled to UI thread for updates.");

                // 1. Update Filter Service
                _filterService.UpdateCollections(
                    _modListManager.VirtualActiveMods,
                    _modListManager.AllInactiveMods
                );

                // 2. Refresh Counts in ModListViewModel
                ModListViewModel.RefreshCounts();

                // 3. Set HasUnsavedChanges Flag
                HasUnsavedChanges = true; // Setter triggers command updates via observation
                OnPropertyChanged(nameof(HasAnyActiveModIssues));

                // 4. Invalidate Commands: Explicit call might still be needed for commands
                //    not directly observing HasUnsavedChanges or IsLoading, or for global UI state.
                CommandManager.InvalidateRequerySuggested();

                Debug.WriteLine("[ModsViewModel] Finished handling ListChanged event on UI thread.");
            });
        }



        // --- IDisposable Implementation ---

        protected override void Dispose(bool disposing)
        {
            // Check the base class flag BEFORE doing anything
            if (_disposed) // Use the inherited _disposed field
            {
                return;
            }

            if (disposing)
            {
                // --- Derived Class Specific Cleanup ---
                Debug.WriteLine("[ModsViewModel] Disposing derived resources...");
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
                // Use 'as' and null-conditional operator for safety
                (ModListViewModel as IDisposable)?.Dispose();
                (ModDetailsViewModel as IDisposable)?.Dispose();
                (ModActionsViewModel as IDisposable)?.Dispose();
                ModListViewModel = null; // Optional: Clear references
                ModDetailsViewModel = null;
                ModActionsViewModel = null;

                Debug.WriteLine("[ModsViewModel] Disposed managed resources.");
                // --- End Derived Class Specific Cleanup ---
            }

            // Dispose unmanaged resources here if any (specific to ModsViewModel)

            // IMPORTANT: Call the base class implementation LAST
            Debug.WriteLine($"[ModsViewModel] Calling base.Dispose({disposing}).");
            base.Dispose(disposing);
            Debug.WriteLine($"[ModsViewModel] Finished Dispose({disposing}). _disposed = {_disposed}");
        }

        ~ModsViewModel()
        {
            Dispose(false);
            Debug.WriteLine("[ModsViewModel] Finalizer called.");
        }
    }
}