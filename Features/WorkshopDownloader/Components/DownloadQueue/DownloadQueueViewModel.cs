#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Threading;
using System.Diagnostics;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Core.Commands.Base;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public class DownloadQueueViewModel : ViewModelBase, IDisposable
    {
        private readonly IDownloadQueueService _queueService;
        private readonly BrowserViewModel _browserViewModel;
        private readonly ISteamCmdService _steamCmdService;
        private readonly DownloadQueueCommandHandler _commandHandler;
        private readonly ModInfoEnricher _modInfoEnricher;

        public BrowserViewModel BrowserViewModel => _browserViewModel;

        private bool _isOperationInProgress;
        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set
            {
                if (SetProperty(ref _isOperationInProgress, value))
                {
                    // Command CanExecute state will auto-update via observation
                    // But we might need to manually update related properties like CanAddMod/CanDownload
                    RefreshDependentProperties();
                }
            }
        }

        private bool _isSteamCmdReady;
        public bool IsSteamCmdReady
        {
            get => _isSteamCmdReady;
            private set
            {
                if (SetProperty(ref _isSteamCmdReady, value))
                {
                    // Command CanExecute state will auto-update via observation
                    // But we might need to manually update related properties like CanAddMod/CanDownload
                    RefreshDependentProperties();
                }
            }
        }

        private bool _canDownload;
        public bool CanDownload
        {
            get => _canDownload;
            // Make setter private as it's calculated internally
            private set => SetProperty(ref _canDownload, value);
        }

        private bool _canAddMod;
        public bool CanAddMod
        {
            get => _canAddMod;
            private set => SetProperty(ref _canAddMod, value);
        }



        public ObservableCollection<DownloadItem> DownloadList => _queueService.Items;

        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand AddModCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand NavigateToUrlCommand { get; }
        public ICommand RemoveItemsCommand { get; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationStarted;
        public event EventHandler? OperationCompleted;
        public event EventHandler? DownloadCompletedAndRefreshNeeded;

        private NotifyCollectionChangedEventHandler? _queueServiceItemsHandler;
        private EventHandler? _browserModInfoAvailabilityHandler;
        private PropertyChangedEventHandler? _browserPropertyChangedHandler;
        // Remove the _steamCmdSetupStateHandler as CommandHandler handles the status update now
        // private EventHandler<bool>? _steamCmdSetupStateHandler;

        public DownloadQueueViewModel(
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService,
            BrowserViewModel browserViewModel,
            Func<CancellationToken> getCancellationToken,
            IModListManager modListManager,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor,
            ILoggerService logger,
            ISteamApiClient steamApiClient
            )
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));

            _modInfoEnricher = new ModInfoEnricher(modListManager);
            _commandHandler = new DownloadQueueCommandHandler(
                queueService, modService, dialogService, updateCheckerService,
                steamCmdService, browserViewModel, getCancellationToken, modListManager, _modInfoEnricher, steamWorkshopQueueProcessor, logger, steamApiClient
            );

            // Initialize properties based on initial handler state
            _isSteamCmdReady = _commandHandler.IsSteamCmdReady;
            // Calculate initial CanAddMod/CanDownload states
            CalculateCanDownload();

            _commandHandler.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, msg);
            _commandHandler.OperationStarted += (s, e) => OperationStarted?.Invoke(this, e); // DownloaderVM handles IsOperationInProgress
            _commandHandler.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, e); // DownloaderVM handles IsOperationInProgress
            _commandHandler.DownloadCompletedAndRefreshNeeded += (s, e) => DownloadCompletedAndRefreshNeeded?.Invoke(this, e);
            // Handler now directly updates IsSteamCmdReady property via SetProperty
            _commandHandler.SteamCmdReadyStatusChanged += (s, e) => RunOnUIThread(() => IsSteamCmdReady = _commandHandler.IsSteamCmdReady);

            _queueServiceItemsHandler = QueueService_ItemsChanged;
            _queueService.Items.CollectionChanged += _queueServiceItemsHandler;

            _browserModInfoAvailabilityHandler = BrowserViewModel_ModInfoAvailabilityChanged;

            _browserPropertyChangedHandler = BrowserViewModel_PropertyChanged;
            _browserViewModel.PropertyChanged += _browserPropertyChangedHandler;

            // Removed: _steamCmdService.SetupStateChanged handler as _commandHandler manages this now.

            // --- Command Creation using ViewModelBase helpers ---

            // SetupSteamCmdCommand: Enabled only if SteamCMD is NOT ready and no operation is in progress
            SetupSteamCmdCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteSetupSteamCmdAsync,
                () => !IsSteamCmdReady && !IsOperationInProgress,
                nameof(IsSteamCmdReady), nameof(IsOperationInProgress) // Observe these properties
            );

            // CheckUpdatesCommand: Enabled only if SteamCMD IS ready, check is possible, and no operation is in progress
            CheckUpdatesCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteCheckUpdatesAsync,
                () => IsSteamCmdReady && _commandHandler.CanExecuteCheckUpdates() && !IsOperationInProgress,
                 nameof(IsSteamCmdReady), nameof(IsOperationInProgress) // Observe these properties (CanExecuteCheckUpdates doesn't change often, so maybe not needed, but IsSteamCmdReady/IsOperationInProgress are key)
            );

            // AddModCommand: Uses the CanAddMod property for CanExecute. Enabled only if requirements met AND SteamCMD is ready.
            AddModCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteAddModAsync,
                () => CanAddMod, // Use local CanAddMod property
                nameof(CanAddMod) // Observe local CanAddMod property
            );

            // DownloadCommand: Uses the CanDownload property for CanExecute. Enabled if list has items, SteamCMD is ready, and no operation.
            DownloadCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteDownloadAsync,
                () => CanDownload, // CanExecute depends solely on the CanDownload property now
                nameof(CanDownload) // Observe the calculated property
            );

            // Sync commands observe IsOperationInProgress to disable during operations
            RemoveItemCommand = CreateCommand<DownloadItem>(
                _commandHandler.ExecuteRemoveItem,
                item => !IsOperationInProgress && item != null,
                nameof(IsOperationInProgress)
            );

            NavigateToUrlCommand = CreateCommand<string>(
                _commandHandler.ExecuteNavigateToUrl,
                url => !IsOperationInProgress && !string.IsNullOrWhiteSpace(url),
                nameof(IsOperationInProgress)
            );

            RemoveItemsCommand = CreateCommand<System.Collections.IList>(
                _commandHandler.ExecuteRemoveItems,
                items => !IsOperationInProgress && items != null && items.Count > 0,
                nameof(IsOperationInProgress)
            );
            // --- End Command Creation ---
            _browserPropertyChangedHandler = BrowserViewModel_PropertyChanged; // Ensure handler exists
            _browserViewModel.PropertyChanged += _browserPropertyChangedHandler; // Ensure subscribed


            RunOnUIThread(() => _modInfoEnricher.EnrichAllDownloadItems(DownloadList));

            CalculateCanAddMod();
            CalculateCanDownload();

        }

        private void CalculateCanDownload()
        {
            CanDownload = DownloadList.Any()
                          && IsSteamCmdReady
                          && !IsOperationInProgress;
        }

        // Call this when IsOperationInProgress or IsSteamCmdReady changes, or list/browser state changes
        private void RefreshDependentProperties()
        {
            RunOnUIThread(() =>
            {
                CalculateCanDownload();
                // Note: Command CanExecute states that *directly* observe IsOperationInProgress/IsSteamCmdReady
                // will update automatically via ViewModelBase's observation mechanism.
                // We only need to recalculate properties like CanAddMod/CanDownload which are then observed by *their* commands.
            });
        }


        private void CalculateCanAddMod()
        {
            // Use the public property now for consistency, or keep using _browserViewModel
            CanAddMod = IsSteamCmdReady
                        && !IsOperationInProgress
                        && BrowserViewModel != null
                        && ((BrowserViewModel.IsValidModUrl && BrowserViewModel.IsModInfoAvailable) || BrowserViewModel.IsCollectionUrl);

            Debug.WriteLine($"[QueueVM] Calculated CanAddMod: {CanAddMod} (IsSteamCmdReady={IsSteamCmdReady}, !InProg={!IsOperationInProgress}, IsValidModUrl={BrowserViewModel?.IsValidModUrl}, IsCollectionUrl={BrowserViewModel?.IsCollectionUrl}, IsModInfoAvailable={BrowserViewModel?.IsModInfoAvailable})");
        }

        private void QueueService_ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RunOnUIThread(() =>
            {
                // Enrichment logic (unchanged)
                bool needsEnrichment = false;
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    _modInfoEnricher.RefreshLocalModLookup();
                    Debug.WriteLine($"[QueueVM] Items added, enriching {e.NewItems.Count} new item(s)...");
                    foreach (DownloadItem newItem in e.NewItems)
                    {
                        _modInfoEnricher.EnrichDownloadItem(newItem);
                    }
                }
                else if (e.Action == NotifyCollectionChangedAction.Reset || e.Action == NotifyCollectionChangedAction.Replace)
                {
                    Debug.WriteLine("[QueueVM] Download list reset or replaced, re-enriching all.");
                    needsEnrichment = true;
                }

                if (needsEnrichment)
                {
                    _modInfoEnricher.EnrichAllDownloadItems(DownloadList);
                }

                // Recalculate CanDownload as list content changed
                CalculateCanDownload();
                // CanAddMod is not affected by queue changes
            });
        }

        private void BrowserViewModel_ModInfoAvailabilityChanged(object? sender, EventArgs e)
        {
            // Recalculate CanAddMod when browser state changes
            RefreshDependentProperties();
        }

        private void BrowserViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BrowserViewModel.IsValidModUrl) ||
                e.PropertyName == nameof(BrowserViewModel.IsModInfoAvailable) ||
                e.PropertyName == nameof(BrowserViewModel.IsCollectionUrl))
            {
                RunOnUIThread(CalculateCanAddMod); // Recalculate when browser state changes
            }
            // No need to handle BrowserViewModel.CanAddMod change anymore
        }


        public void RefreshLocalModInfo()
        {
            RunOnUIThread(() =>
            {
                Debug.WriteLine("[QueueVM] External request to refresh local mod info.");
                _modInfoEnricher.EnrichAllDownloadItems(DownloadList);
                // Recalculate download state as local info might affect future logic (though not CanDownload directly now)
                RefreshDependentProperties();
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                Debug.WriteLine("[QueueVM] Disposing derived resources...");
                _commandHandler?.Cleanup();

                if (_queueServiceItemsHandler != null && _queueService?.Items != null)
                    _queueService.Items.CollectionChanged -= _queueServiceItemsHandler;
                if (_browserPropertyChangedHandler != null && _browserViewModel != null)
                    _browserViewModel.PropertyChanged -= _browserPropertyChangedHandler;


                _queueServiceItemsHandler = null;
                _browserModInfoAvailabilityHandler = null;
                _browserPropertyChangedHandler = null;
                // Removed: _steamCmdSetupStateHandler = null;

                Debug.WriteLine("DownloadQueueViewModel cleaned up (derived resources).");
            }

            Debug.WriteLine($"[QueueVM] Calling base.Dispose({disposing}).");
            base.Dispose(disposing);
            Debug.WriteLine($"[QueueVM] Finished Dispose({disposing}). _disposed = {_disposed}");
        }

        ~DownloadQueueViewModel()
        {
            Dispose(false);
        }
    }
}