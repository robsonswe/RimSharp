#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Threading;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using System.Diagnostics;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq; // Required for PropertyChangedEventArgs

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    /// <summary>
    /// ViewModel for the Download Queue component.
    /// Manages the state of the download queue UI, coordinates with services,
    /// and delegates command execution to DownloadQueueCommandHandler.
    /// </summary>
    public class DownloadQueueViewModel : ViewModelBase
    {
        // Services and Dependencies (only those needed directly by VM or for handler construction)
        private readonly IDownloadQueueService _queueService;
        private readonly BrowserViewModel _browserViewModel; // Needed for CanAddMod logic
        private readonly ISteamCmdService _steamCmdService; // Needed for SetupStateChanged subscription

        // Helpers and Handlers
        private readonly DownloadQueueCommandHandler _commandHandler;
        private readonly ModInfoEnricher _modInfoEnricher;

        // Backing fields for properties managed by this VM
        private bool _isOperationInProgress; // Controlled by parent (DownloaderViewModel)

        // --- Properties for Binding ---
        public ObservableCollection<DownloadItem> DownloadList => _queueService.Items;

        // State properties reflecting handler state or derived state
        public bool IsSteamCmdReady => _commandHandler?.IsSteamCmdReady ?? false;
        public bool CanAddMod => _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress;
        public bool CanDownload => DownloadList.Any() && IsSteamCmdReady && !IsOperationInProgress;

        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set
            {
                // Use SetProperty to handle change notification and avoid unnecessary updates
                if (SetProperty(ref _isOperationInProgress, value))
                {
                    // Update the handler's state when the parent changes this VM's state
                    if (_commandHandler != null) _commandHandler.IsOperationInProgress = value;
                    // Refresh commands and related properties when operation status changes
                    RefreshCommandStates();
                }
            }
        }

        // --- Commands (Exposed from Command Handler) ---
        public ICommand SetupSteamCmdCommand => _commandHandler.SetupSteamCmdCommand;
        public ICommand CheckUpdatesCommand => _commandHandler.CheckUpdatesCommand;
        public ICommand AddModCommand => _commandHandler.AddModCommand;
        public ICommand DownloadCommand => _commandHandler.DownloadCommand;
        public ICommand RemoveItemCommand => _commandHandler.RemoveItemCommand;
        public ICommand NavigateToUrlCommand => _commandHandler.NavigateToUrlCommand;
        public ICommand RemoveItemsCommand => _commandHandler.RemoveItemsCommand;

        // --- Events (Forwarded from Command Handler or raised directly) ---
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationStarted; // Forwarded
        public event EventHandler? OperationCompleted; // Forwarded
        public event EventHandler? DownloadCompletedAndRefreshNeeded; // Forwarded

        // --- Named handlers for unsubscribing ---
        private NotifyCollectionChangedEventHandler? _queueServiceItemsHandler;
        private EventHandler? _browserModInfoAvailabilityHandler;
        private PropertyChangedEventHandler? _browserPropertyChangedHandler;
        private EventHandler<bool>? _steamCmdSetupStateHandler;
        private EventHandler<string>? _commandHandlerStatusHandler;
        private EventHandler? _commandHandlerOperationStartedHandler;
        private EventHandler? _commandHandlerOperationCompletedHandler;
        private EventHandler? _commandHandlerDownloadCompletedHandler;
        private EventHandler? _commandHandlerSteamCmdReadyHandler;


        public DownloadQueueViewModel(
            // Pass all dependencies needed by VM, Enricher, and Handler
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService,
            BrowserViewModel browserViewModel,
            Func<CancellationToken> getCancellationToken,
            IModListManager modListManager)
        {
            // Store direct dependencies
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));

            // Initialize helpers and handlers, passing required dependencies
            _modInfoEnricher = new ModInfoEnricher(modListManager);
            _commandHandler = new DownloadQueueCommandHandler(
                _queueService,
                modService, // Pass through
                dialogService, // Pass through
                updateCheckerService, // Pass through
                _steamCmdService,
                _browserViewModel,
                getCancellationToken, // Pass through
                modListManager, // Pass through
                _modInfoEnricher // Pass the created enricher instance
            );

            // --- Subscribe to Handler Events (Forwarding) ---
            _commandHandlerStatusHandler = (s, msg) => StatusChanged?.Invoke(this, msg);
            _commandHandler.StatusChanged += _commandHandlerStatusHandler;

            _commandHandlerOperationStartedHandler = (s, e) => OperationStarted?.Invoke(this, e);
            _commandHandler.OperationStarted += _commandHandlerOperationStartedHandler;

            _commandHandlerOperationCompletedHandler = (s, e) => OperationCompleted?.Invoke(this, e);
            _commandHandler.OperationCompleted += _commandHandlerOperationCompletedHandler;

            _commandHandlerDownloadCompletedHandler = (s, e) => DownloadCompletedAndRefreshNeeded?.Invoke(this, e);
            _commandHandler.DownloadCompletedAndRefreshNeeded += _commandHandlerDownloadCompletedHandler;

            _commandHandlerSteamCmdReadyHandler = CommandHandler_SteamCmdReadyStatusChanged;
            _commandHandler.SteamCmdReadyStatusChanged += _commandHandlerSteamCmdReadyHandler;


            // --- Subscribe to Service/Child VM Events (for UI updates/enrichment) ---
            _queueServiceItemsHandler = QueueService_ItemsChanged;
            _queueService.Items.CollectionChanged += _queueServiceItemsHandler;

            _browserModInfoAvailabilityHandler = BrowserViewModel_ModInfoAvailabilityChanged;
            _browserViewModel.ModInfoAvailabilityChanged += _browserModInfoAvailabilityHandler;

            _browserPropertyChangedHandler = BrowserViewModel_PropertyChanged;
            _browserViewModel.PropertyChanged += _browserPropertyChangedHandler;

            _steamCmdSetupStateHandler = SteamCmdService_SetupStateChanged;
            _steamCmdService.SetupStateChanged += _steamCmdSetupStateHandler;


            // --- Initial State ---
            _modInfoEnricher.EnrichAllDownloadItems(DownloadList); // Initial enrichment
            RefreshCommandStates(); // Initial check
        }

        // --- Event Handlers ---

        private void CommandHandler_SteamCmdReadyStatusChanged(object? sender, EventArgs e)
        {
            // Handler has updated its internal state, now update VM properties and commands
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(IsSteamCmdReady));
                OnPropertyChanged(nameof(CanDownload));
                RefreshCommandStates(); // Let handler update CanExecute, then update VM props
            });
        }

        private void QueueService_ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RunOnUIThread(() =>
            {
                bool needsEnrichment = false;
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    // Ensure lookup is fresh *before* enriching new items
                    _modInfoEnricher.RefreshLocalModLookup();

                    Debug.WriteLine($"[QueueVM] Items added, enriching {e.NewItems.Count} new item(s)...");
                    foreach (DownloadItem newItem in e.NewItems)
                    {
                        _modInfoEnricher.EnrichDownloadItem(newItem);
                    }
                }
                else if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    // Re-enrich everything if the list is reset
                    Debug.WriteLine("[QueueVM] Download list reset, re-enriching all.");
                    needsEnrichment = true;
                }
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    Debug.WriteLine("[QueueVM] Item removed from download list.");
                    // No enrichment needed typically, but refresh commands
                }
                else if (e.Action == NotifyCollectionChangedAction.Replace)
                {
                     Debug.WriteLine("[QueueVM] Item replaced in download list.");
                     needsEnrichment = true; // Re-enrich the affected items (or all for simplicity)
                }

                if (needsEnrichment)
                {
                    _modInfoEnricher.EnrichAllDownloadItems(DownloadList);
                }

                // Always refresh potentially affected properties/commands
                OnPropertyChanged(nameof(DownloadList)); // Affects UI list itself
                OnPropertyChanged(nameof(CanDownload));
                RefreshCommandStates(); // Tell handler to re-evaluate CanExecute
            });
        }

        private void BrowserViewModel_ModInfoAvailabilityChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(CanAddMod));
                _commandHandler.RefreshCommandStates(); // Tell handler to re-evaluate CanExecute
            });
        }

        private void BrowserViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // React to IsValidModUrl changes from BrowserViewModel
            if (e.PropertyName == nameof(BrowserViewModel.IsValidModUrl))
            {
                RunOnUIThread(() =>
                {
                    OnPropertyChanged(nameof(CanAddMod));
                    _commandHandler.RefreshCommandStates(); // Tell handler to re-evaluate CanExecute
                });
            }
            // Potentially react to other properties if needed
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            // This event triggers the handler to update its internal state via UpdateSteamCmdReadyStatus
            // The handler then raises its *own* event (SteamCmdReadyStatusChanged) which we handle above.
            // This ensures the handler is the source of truth for IsSteamCmdReady.
             _ = _commandHandler.UpdateSteamCmdReadyStatus(); // Fire and forget is okay
        }

        // --- Public Methods ---

        // Central method to refresh command states and related VM properties
        public void RefreshCommandStates()
        {
            // Tell the handler to update its internal command states
            _commandHandler?.RefreshCommandStates();

            // Update VM properties that depend on the handler's state or other conditions
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(CanAddMod));
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(IsSteamCmdReady)); // Ensure this reflects handler state
            });
        }

        // Called externally (e.g., by parent VM) to refresh local info display
        public void RefreshLocalModInfo()
        {
            RunOnUIThread(() =>
            {
                Debug.WriteLine("[QueueVM] External request to refresh local mod info.");
                _modInfoEnricher.EnrichAllDownloadItems(DownloadList);
            });
        }

        // --- Cleanup ---
        public void Cleanup()
        {
            Debug.WriteLine("[QueueVM] Cleaning up...");

            // Unsubscribe from handler events
             if (_commandHandler != null)
             {
                 if (_commandHandlerStatusHandler != null) _commandHandler.StatusChanged -= _commandHandlerStatusHandler;
                 if (_commandHandlerOperationStartedHandler != null) _commandHandler.OperationStarted -= _commandHandlerOperationStartedHandler;
                 if (_commandHandlerOperationCompletedHandler != null) _commandHandler.OperationCompleted -= _commandHandlerOperationCompletedHandler;
                 if (_commandHandlerDownloadCompletedHandler != null) _commandHandler.DownloadCompletedAndRefreshNeeded -= _commandHandlerDownloadCompletedHandler;
                 if (_commandHandlerSteamCmdReadyHandler != null) _commandHandler.SteamCmdReadyStatusChanged -= _commandHandlerSteamCmdReadyHandler;
                 _commandHandler.Cleanup(); // Ask handler to clean up its resources
             }


            // Unsubscribe from service/child VM events
            if (_queueServiceItemsHandler != null)
                _queueService.Items.CollectionChanged -= _queueServiceItemsHandler;
            if (_browserModInfoAvailabilityHandler != null)
                 _browserViewModel.ModInfoAvailabilityChanged -= _browserModInfoAvailabilityHandler;
             if (_browserPropertyChangedHandler != null)
                 _browserViewModel.PropertyChanged -= _browserPropertyChangedHandler;
             if (_steamCmdSetupStateHandler != null)
                 _steamCmdService.SetupStateChanged -= _steamCmdSetupStateHandler;

            // Clear handlers to be safe
            _queueServiceItemsHandler = null;
            _browserModInfoAvailabilityHandler = null;
            _browserPropertyChangedHandler = null;
            _steamCmdSetupStateHandler = null;
            _commandHandlerStatusHandler = null;
            _commandHandlerOperationStartedHandler = null;
            _commandHandlerOperationCompletedHandler = null;
            _commandHandlerDownloadCompletedHandler = null;
            _commandHandlerSteamCmdReadyHandler = null;


            Debug.WriteLine("DownloadQueueViewModel cleaned up.");
        }
    }
}