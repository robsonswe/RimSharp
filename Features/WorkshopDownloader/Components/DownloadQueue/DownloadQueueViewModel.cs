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
                    RefreshDependentProperties();
                }
            }
        }

        private bool _canDownload;
        public bool CanDownload
        {
            get => _canDownload;
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
            _isSteamCmdReady = _commandHandler.IsSteamCmdReady;

            CalculateCanDownload();

            _commandHandler.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, msg);
            _commandHandler.OperationStarted += (s, e) => OperationStarted?.Invoke(this, e); 
            _commandHandler.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, e); 
            _commandHandler.DownloadCompletedAndRefreshNeeded += (s, e) => DownloadCompletedAndRefreshNeeded?.Invoke(this, e);
            _commandHandler.SteamCmdReadyStatusChanged += (s, e) => RunOnUIThread(() => IsSteamCmdReady = _commandHandler.IsSteamCmdReady);

            _queueServiceItemsHandler = QueueService_ItemsChanged;
            _queueService.Items.CollectionChanged += _queueServiceItemsHandler;

            _browserModInfoAvailabilityHandler = BrowserViewModel_ModInfoAvailabilityChanged;

            _browserPropertyChangedHandler = BrowserViewModel_PropertyChanged;
            _browserViewModel.PropertyChanged += _browserPropertyChangedHandler;

            SetupSteamCmdCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteSetupSteamCmdAsync,
                () => !IsSteamCmdReady && !IsOperationInProgress,
                nameof(IsSteamCmdReady), nameof(IsOperationInProgress)
            );

            CheckUpdatesCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteCheckUpdatesAsync,
                () => IsSteamCmdReady && _commandHandler.CanExecuteCheckUpdates() && !IsOperationInProgress,
                 nameof(IsSteamCmdReady), nameof(IsOperationInProgress) 
            );

            AddModCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteAddModAsync,
                () => CanAddMod, 
                nameof(CanAddMod) 
            );

            DownloadCommand = CreateCancellableAsyncCommand(
                _commandHandler.ExecuteDownloadAsync,
                () => CanDownload, 
                nameof(CanDownload) 
            );

            RemoveItemCommand = CreateAsyncCommand<DownloadItem>(
                _commandHandler.ExecuteRemoveItemAsync,
                item => !IsOperationInProgress && item != null,
                nameof(IsOperationInProgress)
            );

            NavigateToUrlCommand = CreateAsyncCommand<string>(
                _commandHandler.ExecuteNavigateToUrl,
                url => !IsOperationInProgress && !string.IsNullOrWhiteSpace(url),
                nameof(IsOperationInProgress)
            );

            RemoveItemsCommand = CreateAsyncCommand<System.Collections.IList>(
                _commandHandler.ExecuteRemoveItemsAsync,
                items => !IsOperationInProgress && items != null && items.Count > 0,
                nameof(IsOperationInProgress)
            );

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

        private void RefreshDependentProperties()
        {
            RunOnUIThread(() =>
            {
                CalculateCanDownload();
                CalculateCanAddMod();
            });
        }

        private void CalculateCanAddMod()
        {
            bool alreadyInQueue = false;
            if (BrowserViewModel != null && !string.IsNullOrEmpty(BrowserViewModel.CurrentPageSteamId))
            {
                alreadyInQueue = DownloadList.Any(m => m.SteamId == BrowserViewModel.CurrentPageSteamId);
            }

            CanAddMod = IsSteamCmdReady
                        && !IsOperationInProgress
                        && BrowserViewModel != null
                        && !alreadyInQueue
                        && ((BrowserViewModel.IsValidModUrl && BrowserViewModel.IsModInfoAvailable) || BrowserViewModel.IsCollectionUrl);

            Debug.WriteLine($"[QueueVM] Calculated CanAddMod: {CanAddMod} (AlreadyInQueue={alreadyInQueue}, IsSteamCmdReady={IsSteamCmdReady}, !InProg={!IsOperationInProgress}, IsValidModUrl={BrowserViewModel?.IsValidModUrl}, IsCollectionUrl={BrowserViewModel?.IsCollectionUrl}, IsModInfoAvailable={BrowserViewModel?.IsModInfoAvailable})");
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
                CalculateCanDownload();
                CalculateCanAddMod();
            });
        }

        private void BrowserViewModel_ModInfoAvailabilityChanged(object? sender, EventArgs e)
        {
            RefreshDependentProperties();
        }

        private void BrowserViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BrowserViewModel.IsValidModUrl) ||
                e.PropertyName == nameof(BrowserViewModel.IsModInfoAvailable) ||
                e.PropertyName == nameof(BrowserViewModel.IsCollectionUrl) ||
                e.PropertyName == nameof(BrowserViewModel.CurrentPageSteamId))
            {
                RunOnUIThread(CalculateCanAddMod); 
            }
        }

        public void RefreshLocalModInfo()
        {
            RunOnUIThread(() =>
            {
                Debug.WriteLine("[QueueVM] External request to refresh local mod info.");
                _modInfoEnricher.EnrichAllDownloadItems(DownloadList);
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
