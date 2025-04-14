#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Threading;
using System.Diagnostics;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using RimSharp.Features.WorkshopDownloader.Components.Browser;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public class DownloadQueueViewModel : ViewModelBase, IDisposable
    {
        private readonly IDownloadQueueService _queueService;
        private readonly BrowserViewModel _browserViewModel;
        private readonly ISteamCmdService _steamCmdService;
        private readonly DownloadQueueCommandHandler _commandHandler;
        private readonly ModInfoEnricher _modInfoEnricher;
        private bool _isDisposed = false;

        private bool _isOperationInProgress;
        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set
            {
                // Use SetProperty provided by ViewModelBase
                if (SetProperty(ref _isOperationInProgress, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        private bool _isSteamCmdReady;
        public bool IsSteamCmdReady
        {
            get => _isSteamCmdReady;
            private set => SetProperty(ref _isSteamCmdReady, value);
        }

        private bool _canAddMod;
        public bool CanAddMod
        {
            get => _canAddMod;
            private set => SetProperty(ref _canAddMod, value);
        }

        private bool _canDownload;
        public bool CanDownload
        {
            get => _canDownload;
            private set => SetProperty(ref _canDownload, value);
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
        private EventHandler<bool>? _steamCmdSetupStateHandler;

        public DownloadQueueViewModel(
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService,
            BrowserViewModel browserViewModel,
            Func<CancellationToken> getCancellationToken,
            IModListManager modListManager)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));

            _modInfoEnricher = new ModInfoEnricher(modListManager);
            _commandHandler = new DownloadQueueCommandHandler(
                queueService, modService, dialogService, updateCheckerService,
                steamCmdService, browserViewModel, getCancellationToken, modListManager, _modInfoEnricher
            );

            _isSteamCmdReady = _commandHandler.IsSteamCmdReady;
            _canAddMod = _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress;
            _canDownload = DownloadList.Any() && IsSteamCmdReady && !IsOperationInProgress;

            _commandHandler.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, msg);
            _commandHandler.OperationStarted += (s, e) => OperationStarted?.Invoke(this, e);
            _commandHandler.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, e);
            _commandHandler.DownloadCompletedAndRefreshNeeded += (s, e) => DownloadCompletedAndRefreshNeeded?.Invoke(this, e);
            _commandHandler.SteamCmdReadyStatusChanged += (s, e) => RunOnUIThread(() => IsSteamCmdReady = _commandHandler.IsSteamCmdReady);

            _queueServiceItemsHandler = QueueService_ItemsChanged;
            _queueService.Items.CollectionChanged += _queueServiceItemsHandler;

            _browserModInfoAvailabilityHandler = BrowserViewModel_ModInfoAvailabilityChanged;
            _browserViewModel.ModInfoAvailabilityChanged += _browserModInfoAvailabilityHandler;

            _browserPropertyChangedHandler = BrowserViewModel_PropertyChanged;
            _browserViewModel.PropertyChanged += _browserPropertyChangedHandler;

            _steamCmdSetupStateHandler = SteamCmdService_SetupStateChanged;
            _steamCmdService.SetupStateChanged += _steamCmdSetupStateHandler;

            // Use CreateCancellableAsyncCommand for async commands with CancellationToken
            SetupSteamCmdCommand = CreateCancellableAsyncCommand(_commandHandler.ExecuteSetupSteamCmdAsync, () => !IsOperationInProgress, nameof(IsOperationInProgress));
            CheckUpdatesCommand = CreateCancellableAsyncCommand(_commandHandler.ExecuteCheckUpdatesAsync, _commandHandler.CanExecuteCheckUpdates, nameof(IsOperationInProgress));
            AddModCommand = CreateCancellableAsyncCommand(_commandHandler.ExecuteAddModAsync, () => CanAddMod, nameof(CanAddMod), nameof(IsOperationInProgress));
            DownloadCommand = CreateCancellableAsyncCommand(_commandHandler.ExecuteDownloadAsync, () => CanDownload, nameof(CanDownload), nameof(IsOperationInProgress));

            // Sync commands use CreateCommand
            RemoveItemCommand = CreateCommand<DownloadItem>(_commandHandler.ExecuteRemoveItem, item => !IsOperationInProgress && item != null, nameof(IsOperationInProgress));
            NavigateToUrlCommand = CreateCommand<string>(_commandHandler.ExecuteNavigateToUrl, url => !IsOperationInProgress && !string.IsNullOrWhiteSpace(url), nameof(IsOperationInProgress));
            RemoveItemsCommand = CreateCommand<System.Collections.IList>(_commandHandler.ExecuteRemoveItems, items => !IsOperationInProgress && items != null && items.Count > 0, nameof(IsOperationInProgress));

            RunOnUIThread(() => _modInfoEnricher.EnrichAllDownloadItems(DownloadList));
        }

        private void QueueService_ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RunOnUIThread(() =>
            {
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

                // Force property change notification
                CanDownload = DownloadList.Any() && IsSteamCmdReady && !IsOperationInProgress;
                OnPropertyChanged(nameof(CanDownload)); // Explicit notification
            });
        }

        private void BrowserViewModel_ModInfoAvailabilityChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() => CanAddMod = _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress);
        }

        private void BrowserViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BrowserViewModel.IsValidModUrl))
            {
                RunOnUIThread(() => CanAddMod = _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress);
            }
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() => _ = _commandHandler.UpdateSteamCmdReadyStatusAsync());
        }
        public void RefreshCommandStates()
        {
            RunOnUIThread(() =>
            {
                CanDownload = DownloadList.Any() && IsSteamCmdReady && !IsOperationInProgress;
                CanAddMod = _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress;
            });
        }

        public void RefreshLocalModInfo()
        {
            RunOnUIThread(() =>
            {
                Debug.WriteLine("[QueueVM] External request to refresh local mod info.");
                _modInfoEnricher.EnrichAllDownloadItems(DownloadList);
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                Debug.WriteLine("[QueueVM] Disposing...");
                _commandHandler?.Cleanup();

                if (_queueServiceItemsHandler != null && _queueService?.Items != null)
                    _queueService.Items.CollectionChanged -= _queueServiceItemsHandler;
                if (_browserModInfoAvailabilityHandler != null && _browserViewModel != null)
                    _browserViewModel.ModInfoAvailabilityChanged -= _browserModInfoAvailabilityHandler;
                if (_browserPropertyChangedHandler != null && _browserViewModel != null)
                    _browserViewModel.PropertyChanged -= _browserPropertyChangedHandler;
                if (_steamCmdSetupStateHandler != null && _steamCmdService != null)
                    _steamCmdService.SetupStateChanged -= _steamCmdSetupStateHandler;

                _queueServiceItemsHandler = null;
                _browserModInfoAvailabilityHandler = null;
                _browserPropertyChangedHandler = null;
                _steamCmdSetupStateHandler = null;

                Debug.WriteLine("DownloadQueueViewModel cleaned up.");
            }

            _isDisposed = true;
        }

        public void Cleanup()
        {
            Dispose();
        }

        ~DownloadQueueViewModel()
        {
            Dispose(false);
        }
    }
}