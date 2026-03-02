#nullable enable
using RimSharp.Features.ModManager.ViewModels.Actions;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModsViewModel : ViewModelBase, IDisposable, IModsViewModel
    {
        private readonly IModDataService _dataService;
        private readonly IModFilterService _filterService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IPathService _pathService;
        private readonly IModService _modService;
        private readonly IModDeletionService _deletionService;
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor;
        private readonly IGitService _gitService;

        public ModListViewModel ModListViewModel { get; private set; }
        public ModDetailsViewModel ModDetailsViewModel { get; private set; }
        public ModActionsViewModel ModActionsViewModel { get; private set; }

        public ICommand RequestRefreshCommand { get; }
        private bool _isLoading;
        private bool _hasUnsavedChanges;
        private ModItem? _selectedMod;
        private IList? _selectedItems; 

        public bool HasAnyActiveModIssues => _modListManager?.HasAnyActiveModIssues ?? false;

        private string _loadingMessage = string.Empty;
        public string LoadingMessage
        {
            get => _loadingMessage;
            private set => SetProperty(ref _loadingMessage, value);
        }

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
                }
            }
        }

        private bool _isViewActive;
        public bool IsViewActive
        {
            get => _isViewActive;
            set
            {
                if (SetProperty(ref _isViewActive, value))
                {
                    if (value)
                    {
                        EnsureSelection();
                    }

                    if (ModActionsViewModel != null)
                        ModActionsViewModel.IsViewActive = value;
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
                    if (ModActionsViewModel != null) ModActionsViewModel.HasUnsavedChanges = value;
                }
            }
        }

        private void EnsureSelection()
        {
            if (SelectedMod != null) return;

            ModItem? candidate = null;

            if (_modListManager.VirtualActiveMods.Any())
            {
                candidate = _modListManager.VirtualActiveMods.FirstOrDefault(vm => vm.Mod.ModType == ModType.Core).Mod ??
                            _modListManager.VirtualActiveMods.FirstOrDefault().Mod;
            }

            if (candidate == null)
            {
                candidate = _modListManager.AllInactiveMods.FirstOrDefault();
            }

            if (candidate != null)
            {
                SelectedMod = candidate;
            }
        }

        public ModItem? SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (SetProperty(ref _selectedMod, value))
                {
                    if (ModDetailsViewModel != null) ModDetailsViewModel.CurrentMod = value;
                    if (ModActionsViewModel != null) ModActionsViewModel.SelectedMod = value; 
                }
            }
        }

        public IList? SelectedItems
        {
            get => _selectedItems;
            set
            {
                if (SetProperty(ref _selectedItems, value))
                {
                    if (ModActionsViewModel != null) ModActionsViewModel.SelectedItems = value; 
                }
            }
        }

        public ModsViewModel(
            IModDataService dataService,
            IModFilterService filterService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IModDuplicateService duplicateService,
            IModDeletionService deletionService,
            IDialogService dialogService,
            IModService modService,
            IPathService pathService,
            IModRulesService rulesService,
            IModReplacementService replacementService,
            IDownloadQueueService downloadQueueService,
            ISteamApiClient steamApiClient,
            IApplicationNavigationService navigationService,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor,
            IGitService gitService)
        {
            _dataService = dataService;
            _filterService = filterService;
            _modListManager = modListManager;
            _dialogService = dialogService;
            _pathService = pathService;
            _modService = modService;
            _deletionService = deletionService;
            _steamWorkshopQueueProcessor = steamWorkshopQueueProcessor;
            _gitService = gitService;

            RequestRefreshCommand = CreateCancellableAsyncCommand(ExecuteRequestRefreshAsync, CanExecuteRequestRefresh, nameof(IsLoading));

            ModListViewModel = new ModListViewModel(_filterService, _modListManager, commandService, _dialogService);
            ModDetailsViewModel = new ModDetailsViewModel(_dialogService);
            ModActionsViewModel = new ModActionsViewModel(
                _dataService, commandService, ioService, _modListManager, incompatibilityService, duplicateService, _deletionService, _dialogService, _pathService, _modService,
                        replacementService, downloadQueueService, steamApiClient, navigationService, steamWorkshopQueueProcessor, _gitService
                        );
            _modListManager.ListChanged += OnModListManagerChanged;
            ModListViewModel.RequestSelectionChange += OnChildRequestSelectionChange;
            ModActionsViewModel.IsLoadingRequest += OnChildRequestLoading;
            ModActionsViewModel.RequestDataRefresh += OnChildRequestDataRefresh;
            ModActionsViewModel.HasUnsavedChangesRequest += OnChildRequestHasUnsavedChanges;
        }

        public async Task InitializeAsync(IProgress<(int current, int total, string message)>? progress = null)
        {
            await LoadDataAsync(CancellationToken.None, true, progress);
        }

        private bool CanExecuteRequestRefresh()
        {
            return !IsLoading;
        }

        private async Task ExecuteRequestRefreshAsync(CancellationToken ct)
        {
            if (IsLoading)
            {
                Debug.WriteLine("[ModsViewModel] Refresh request ignored, already loading.");
                return;
            }

            Debug.WriteLine("[ModsViewModel] Executing refresh request...");
            try
            {
                RunOnUIThread(() =>
                {
                    Debug.WriteLine("[ModsViewModel] Refreshing PathService and ModActionsViewModel path validity...");
                    _pathService.RefreshPaths(); 
                    ModActionsViewModel?.RefreshPathValidity(); 
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModsViewModel] Error during path refresh: {ex.Message}");
            }

            await RefreshDataAsync(ct); 
            Debug.WriteLine("[ModsViewModel] Refresh execution complete.");
        }

        private void OnChildRequestSelectionChange(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModListViewModel.SelectedMod) && sender is ModListViewModel listVM)
            {
                if (SelectedMod != listVM.SelectedMod)
                {
                    SelectedMod = listVM.SelectedMod;
                }
            }
        }

        private void OnChildRequestLoading(object? sender, bool isLoading)
        {
            IsLoading = isLoading; 
        }

        private void OnChildRequestDataRefresh(object? sender, EventArgs e)
        {
            Debug.WriteLine("[ModsViewModel] Received request for data refresh from child. Executing RequestRefreshCommand.");
            if (RequestRefreshCommand.CanExecute(null))
            {
                RequestRefreshCommand.Execute(null); 
            }
            else
            {
                Debug.WriteLine("[ModsViewModel] Cannot execute refresh command now (likely loading).");
            }
        }

        private void OnChildRequestHasUnsavedChanges(object? sender, bool hasChanges)
        {
            Debug.WriteLine($"[ModsViewModel] Received HasUnsavedChangesRequest: {hasChanges}. Setting parent state.");
            HasUnsavedChanges = hasChanges; 
        }

        public async Task RefreshDataAsync(CancellationToken ct = default)
        {
            Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Starting refresh...");
            if (IsLoading)
            {
                Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Refresh aborted, already loading.");
                return;
            }
            await LoadDataAsync(ct); 
            Debug.WriteLine("[ModsViewModel - RefreshDataAsync] Refresh complete.");
        }

        private async Task LoadDataAsync(CancellationToken ct = default, bool isInitialLoad = false, IProgress<(int current, int total, string message)>? externalProgress = null)
        {
            if (IsLoading) return; 

            RunOnUIThread(() =>
            {
                Debug.WriteLine("[ModsViewModel] Initial path refresh during LoadDataAsync...");
                _pathService.RefreshPaths();
                ModActionsViewModel?.RefreshPathValidity();
            });

            IsLoading = true; 
            ProgressDialogViewModel? progressDialog = null;

            try
            {
                ct.ThrowIfCancellationRequested();

                if (!isInitialLoad)
                {
                    await RunOnUIThreadAsync(() =>
                    {
                        var dialogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        progressDialog = _dialogService.ShowProgressDialog("Loading Mods", "Initializing...", false, false, dialogCts, false);
                    });
                }

                ct.ThrowIfCancellationRequested();

                var progressReporter = new Progress<(int current, int total, string message)>(update =>
                {
                    RunOnUIThread(() =>
                    {
                        if (progressDialog != null)
                        {
                            progressDialog.Message = update.message;
                            progressDialog.Progress = (int)((double)update.current / update.total * 100);
                        }

                        externalProgress?.Report(update);
                        LoadingMessage = update.message;
                    });
                });

                var allMods = await _dataService.LoadAllModsAsync(progressReporter);
                ct.ThrowIfCancellationRequested();

                string msg = "Syncing active mods...";
                RunOnUIThread(() =>
                {
                    if (progressDialog != null) progressDialog.Message = msg;
                    if (isInitialLoad) externalProgress?.Report((98, 100, msg));
                });

                var activeIdsFromConfig = await Task.Run(() => _dataService.LoadActiveModIdsFromConfig(), ct);
                ct.ThrowIfCancellationRequested();

                RunOnUIThread(() =>
                {
                    if (progressDialog != null)
                    {
                        progressDialog.Message = "Initializing UI...";
                        progressDialog.Progress = 99;
                    }
                    if (isInitialLoad) externalProgress?.Report((99, 100, "Initializing UI..."));
                });

                _modListManager.Initialize(allMods, activeIdsFromConfig);
                ct.ThrowIfCancellationRequested();

                _filterService.UpdateCollections(
                    _modListManager.VirtualActiveMods,
                    _modListManager.AllInactiveMods
                );
                ct.ThrowIfCancellationRequested();

                ModListViewModel.RefreshCounts();

                msg = "Finalizing...";
                LoadingMessage = msg;
                if (progressDialog != null) progressDialog.Message = msg;

                ModItem? selectedModCandidate = null;

                if (_modListManager.VirtualActiveMods.Any())
                {
                    selectedModCandidate = _modListManager.VirtualActiveMods.FirstOrDefault(vm => vm.Mod.ModType == ModType.Core).Mod ??
                                           _modListManager.VirtualActiveMods.FirstOrDefault().Mod;
                }

                if (selectedModCandidate == null)
                {
                    selectedModCandidate = _modListManager.AllInactiveMods.FirstOrDefault();
                }

                if (selectedModCandidate != null)
                {
                    SelectedMod = selectedModCandidate;
                }

                HasUnsavedChanges = false; 

                if (progressDialog != null) progressDialog.CompleteOperation("Mods loaded successfully");
            }
            catch (OperationCanceledException)
            {
                if (progressDialog != null) progressDialog.ForceClose(); 
                Debug.WriteLine("[ModsViewModel] LoadDataAsync cancelled.");

                await RunOnUIThreadAsync(() => _dialogService.ShowWarning("Load Cancelled", "Mod loading was cancelled."));
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                if (progressDialog != null) progressDialog.ForceClose();
                Debug.WriteLine($"Error loading mods: {ex}");
                await RunOnUIThreadAsync(() => _dialogService.ShowError("Loading Error", $"Error loading mods: {ex.Message}"));
                HasUnsavedChanges = false;
            }
            finally
            {
                Debug.WriteLine($"[ModsViewModel] LoadDataAsync FINALLY block entered. Setting IsLoading = false.");
                IsLoading = false; 
                LoadingMessage = string.Empty;
                Debug.WriteLine($"[ModsViewModel] LoadDataAsync FINALLY block finished. IsLoading should be false.");
            }
        }

        private void OnModListManagerChanged(object? sender, ModListChangedEventArgs e)
        {
            Debug.WriteLine($"[ModsViewModel] Handling ModListManager ListChanged event. ActiveModified: {e.ActiveListModified}");

            RunOnUIThread(() =>
            {
                Debug.WriteLine("[ModsViewModel] Marshalled to UI thread for updates.");
                _filterService.UpdateCollections(
                    _modListManager.VirtualActiveMods,
                    _modListManager.AllInactiveMods
                );
                ModListViewModel.RefreshCounts();
                if (SelectedMod != null && !_modListManager.GetAllMods().Contains(SelectedMod))
                {
                    Debug.WriteLine("[ModsViewModel] Selected mod was removed. Resetting selection to default (Core).");
                    SelectedMod = null;
                    EnsureSelection();
                }
                if (e.ActiveListModified)
                {
                    HasUnsavedChanges = true; 
                }

                OnPropertyChanged(nameof(HasAnyActiveModIssues));
                Debug.WriteLine("[ModsViewModel] Finished handling ListChanged event on UI thread.");
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) 
            {
                return;
            }

            if (disposing)
            {
                Debug.WriteLine("[ModsViewModel] Disposing derived resources...");

                if (_modListManager != null) _modListManager.ListChanged -= OnModListManagerChanged;
                if (ModListViewModel != null) ModListViewModel.RequestSelectionChange -= OnChildRequestSelectionChange;
                if (ModActionsViewModel != null)
                {
                    ModActionsViewModel.IsLoadingRequest -= OnChildRequestLoading;
                    ModActionsViewModel.RequestDataRefresh -= OnChildRequestDataRefresh;
                    ModActionsViewModel.HasUnsavedChangesRequest -= OnChildRequestHasUnsavedChanges;
                }

                (ModListViewModel as IDisposable)?.Dispose();
                (ModDetailsViewModel as IDisposable)?.Dispose();
                (ModActionsViewModel as IDisposable)?.Dispose();
                ModListViewModel = null!; 
                ModDetailsViewModel = null!;
                ModActionsViewModel = null!;

                Debug.WriteLine("[ModsViewModel] Disposed managed resources.");
            }

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
