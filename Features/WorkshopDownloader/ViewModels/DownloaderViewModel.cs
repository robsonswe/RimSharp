#nullable enable
using System;
using System.Threading.Tasks;
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Features.WorkshopDownloader.Components.DownloadQueue;
using RimSharp.Features.WorkshopDownloader.Components.StatusBar;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Threading;
using System.Diagnostics;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.ViewModels;

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    public class DownloaderViewModel : ViewModelBase, IDisposable
    {
        public BrowserViewModel BrowserViewModel { get; }
        public DownloadQueueViewModel QueueViewModel { get; }
        public StatusBarViewModel StatusBarViewModel { get; }

        private readonly ISteamCmdService _steamCmdService;
        private readonly IDialogService _dialogService;

        private CancellationTokenSource? _currentOperationCts;
        private bool _isDisposed = false;

        private bool _isOperationInProgress;
        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            private set => SetProperty(ref _isOperationInProgress, value);
        }

        private bool _isSteamCmdReady;
        public bool IsSteamCmdReady
        {
            get => _isSteamCmdReady;
            private set => SetProperty(ref _isSteamCmdReady, value);
        }

        public ICommand CancelOperationCommand { get; }

        public event EventHandler? DownloadCompletedAndRefreshNeeded;

        private EventHandler<string>? _browserStatusHandler;
        private EventHandler<string>? _queueStatusHandler;
        private EventHandler? _queueOperationStartedHandler;
        private EventHandler? _queueOperationCompletedHandler;
        private EventHandler? _queueDownloadCompletedHandler;
        private EventHandler<bool>? _steamCmdSetupStateChangedHandler;

        public DownloaderViewModel(
            IWebNavigationService navigationService,
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService,
            IModListManager modListManager)
        {
            _dialogService = dialogService;
            _steamCmdService = steamCmdService;

            Func<CancellationToken> getCancellationTokenFunc = GetCancellationToken;

            BrowserViewModel = new BrowserViewModel(navigationService, this);
            QueueViewModel = new DownloadQueueViewModel(
                queueService, modService, dialogService, updateCheckerService,
                steamCmdService, BrowserViewModel, getCancellationTokenFunc, modListManager
            );
            StatusBarViewModel = new StatusBarViewModel();

            _browserStatusHandler = (s, message) => StatusBarViewModel.SetStatus(message);
            BrowserViewModel.StatusChanged += _browserStatusHandler;

            _queueStatusHandler = (s, message) => StatusBarViewModel.SetStatus(message);
            QueueViewModel.StatusChanged += _queueStatusHandler;

            _queueOperationStartedHandler = QueueViewModel_OperationStarted;
            QueueViewModel.OperationStarted += _queueOperationStartedHandler;

            _queueOperationCompletedHandler = QueueViewModel_OperationCompleted;
            QueueViewModel.OperationCompleted += _queueOperationCompletedHandler;

            _queueDownloadCompletedHandler = QueueViewModel_DownloadCompletedAndRefreshNeeded;
            QueueViewModel.DownloadCompletedAndRefreshNeeded += _queueDownloadCompletedHandler;

            _steamCmdSetupStateChangedHandler = SteamCmdService_SetupStateChanged;
            _steamCmdService.SetupStateChanged += _steamCmdSetupStateChangedHandler;

            CancelOperationCommand = CreateCommand(
                ExecuteCancelOperation,
                () => IsOperationInProgress && _currentOperationCts != null && !_currentOperationCts.IsCancellationRequested,
                nameof(IsOperationInProgress)
            );

            _ = UpdateSteamCmdReadyStatus();
        }

        private void QueueViewModel_OperationStarted(object? sender, EventArgs e)
        {
            SetOperationInProgressInternal(true, "Operation started...");
        }

        private void QueueViewModel_OperationCompleted(object? sender, EventArgs e)
        {
            SetOperationInProgressInternal(false, "Operation finished.");
        }

        private void QueueViewModel_DownloadCompletedAndRefreshNeeded(object? sender, EventArgs e)
        {
            Debug.WriteLine("[DownloaderVM] Received DownloadCompletedAndRefreshNeeded from QueueVM.");
            DownloadCompletedAndRefreshNeeded?.Invoke(this, e);
            RunOnUIThread(QueueViewModel.RefreshLocalModInfo);
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() => IsSteamCmdReady = isSetup);
        }

        public void SetWebView(WebView2 webView)
        {
            BrowserViewModel.SetWebView(webView);
            // No need to refresh BrowserViewModel commands; handled by observation
        }

        private void SetOperationInProgressInternal(bool inProgress, string statusMessage = "")
        {
            bool changed = false;

            if (inProgress && _currentOperationCts == null)
            {
                _currentOperationCts = new CancellationTokenSource();
                changed = true;
                Debug.WriteLine("[DownloaderVM] SetOperationInProgressInternal(true)");
            }
            else if (!inProgress && _currentOperationCts != null)
            {
                var ctsToDispose = _currentOperationCts;
                _currentOperationCts = null;
                try { ctsToDispose.Cancel(); } catch (ObjectDisposedException) { }
                ctsToDispose.Dispose();
                changed = true;
                Debug.WriteLine("[DownloaderVM] SetOperationInProgressInternal(false), CTS disposed.");
            }

            if (changed)
            {
                IsOperationInProgress = inProgress; // Update DownloaderVM's own property
                RunOnUIThread(() =>
                {
                    // This assignment will now trigger the setter logic in QueueViewModel
                    QueueViewModel.IsOperationInProgress = inProgress;
                    // Ensure BrowserViewModel also reacts correctly if needed
                    // BrowserViewModel.IsOperationInProgress = inProgress; // If BrowserVM needs direct setting
                    Debug.WriteLine($"[DownloaderVM] Notified QueueVM: IsOperationInProgress = {inProgress}.");
                });

                if (!string.IsNullOrEmpty(statusMessage))
                    StatusBarViewModel.SetStatus(statusMessage);
                else if (!inProgress)
                    StatusBarViewModel.SetStatus("Ready");
            }
        }

        public CancellationToken GetCancellationToken()
        {
            return _currentOperationCts?.Token ?? CancellationToken.None;
        }

        private async Task UpdateSteamCmdReadyStatus()
        {
            await _steamCmdService.CheckSetupAsync();
        }

        private void ExecuteCancelOperation()
        {
            if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
            {
                StatusBarViewModel.SetStatus("Attempting to cancel current operation...");
                Debug.WriteLine("[DownloaderVM] Requesting cancellation via CTS.");
                try
                {
                    _currentOperationCts.Cancel();
                    StatusBarViewModel.SetStatus("Cancellation requested. Operation will stop when possible.");
                }
                catch (ObjectDisposedException) { Debug.WriteLine("[DownloaderVM] Tried to cancel an already disposed CTS."); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DownloaderVM] Error during cancellation request: {ex.Message}");
                    StatusBarViewModel.SetStatus("Error requesting cancellation.");
                }
            }
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
                Debug.WriteLine("[DownloaderVM] Disposing...");
                if (_browserStatusHandler != null) BrowserViewModel.StatusChanged -= _browserStatusHandler;
                if (_queueStatusHandler != null) QueueViewModel.StatusChanged -= _queueStatusHandler;
                if (_queueOperationStartedHandler != null) QueueViewModel.OperationStarted -= _queueOperationStartedHandler;
                if (_queueOperationCompletedHandler != null) QueueViewModel.OperationCompleted -= _queueOperationCompletedHandler;
                if (_queueDownloadCompletedHandler != null) QueueViewModel.DownloadCompletedAndRefreshNeeded -= _queueDownloadCompletedHandler;
                if (_steamCmdSetupStateChangedHandler != null) _steamCmdService.SetupStateChanged -= _steamCmdSetupStateChangedHandler;

                (BrowserViewModel as IDisposable)?.Dispose();
                (QueueViewModel as IDisposable)?.Dispose();

                _currentOperationCts?.Cancel();
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;

                _browserStatusHandler = null;
                _queueStatusHandler = null;
                _queueOperationStartedHandler = null;
                _queueOperationCompletedHandler = null;
                _queueDownloadCompletedHandler = null;
                _steamCmdSetupStateChangedHandler = null;

                Debug.WriteLine("[DownloaderVM] Dispose complete.");
            }

            _isDisposed = true;
        }

        public void Cleanup()
        {
            Dispose();
        }

        ~DownloaderViewModel()
        {
            Dispose(false);
        }
    }
}