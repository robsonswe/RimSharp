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
// using RimSharp.Infrastructure.Workshop; // May not be needed directly here anymore
using System.Diagnostics;
using Microsoft.Web.WebView2.Wpf; // Assuming this is needed for SetWebView
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Services; // For service interfaces
using RimSharp.Features.WorkshopDownloader.ViewModels; // Namespace for this VM

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    /// <summary>
    /// Orchestrator ViewModel that coordinates the sub-components of the Workshop Downloader
    /// </summary>
    public class DownloaderViewModel : ViewModelBase
    {
        // Child ViewModels
        public BrowserViewModel BrowserViewModel { get; }
        public DownloadQueueViewModel QueueViewModel { get; } // Type remains the same
        public StatusBarViewModel StatusBarViewModel { get; }

        // Services (Keep those needed for this VM's direct logic or child construction)
        private readonly ISteamCmdService _steamCmdService;
        private readonly IDialogService _dialogService; // Keep if needed for other dialogs maybe?
        // private readonly IModService _modService; // Likely not needed directly anymore
        // private readonly IModListManager _modListManager; // Likely not needed directly anymore

        // Operational state
        private CancellationTokenSource? _currentOperationCts;
        // IsOperationInProgress derived from _currentOperationCts
        public bool IsOperationInProgress => _currentOperationCts != null && !_currentOperationCts.IsCancellationRequested;
        // IsSteamCmdReady can be fetched from QueueViewModel if needed, or keep direct subscription
        public bool IsSteamCmdReady => _steamCmdService?.IsSetupComplete ?? false;

        // Cancel operation command
        public ICommand CancelOperationCommand { get; }

        // Event to forward from DownloadQueueViewModel
        public event EventHandler? DownloadCompletedAndRefreshNeeded;

        // Store named handlers for unsubscribing
        private EventHandler<string>? _browserStatusHandler;
        private EventHandler<string>? _queueStatusHandler;
        private EventHandler? _queueOperationStartedHandler;
        private EventHandler? _queueOperationCompletedHandler;
        private EventHandler? _queueDownloadCompletedHandler;
        private EventHandler<bool>? _steamCmdSetupStateChangedHandler; // Keep direct subscription?


        public DownloaderViewModel(
            // Inject all services needed by children + self
            IWebNavigationService navigationService,
            IDownloadQueueService queueService,
            IModService modService, // Needed for QueueVM constructor
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService, // Needed for QueueVM constructor
            ISteamCmdService steamCmdService,
            IModListManager modListManager) // Needed for QueueVM constructor
        {
            // Store direct dependencies if needed
            _dialogService = dialogService;
            _steamCmdService = steamCmdService;

            // --- Initialize child ViewModels ---
            // Pass the necessary Func<CancellationToken>
            Func<CancellationToken> getCancellationTokenFunc = GetCancellationToken;

            BrowserViewModel = new BrowserViewModel(navigationService, this); // 'this' might be passed for parent interaction
            QueueViewModel = new DownloadQueueViewModel(
                queueService,
                modService,
                dialogService,
                updateCheckerService,
                steamCmdService,
                BrowserViewModel, // Pass BrowserVM reference
                getCancellationTokenFunc, // Pass delegate
                modListManager
            );
            StatusBarViewModel = new StatusBarViewModel();

            // --- Subscribe to status/operation updates from child ViewModels ---
            _browserStatusHandler = (s, message) => StatusBarViewModel.SetStatus(message);
            BrowserViewModel.StatusChanged += _browserStatusHandler;

            _queueStatusHandler = (s, message) => StatusBarViewModel.SetStatus(message);
            QueueViewModel.StatusChanged += _queueStatusHandler;

            _queueOperationStartedHandler = QueueViewModel_OperationStarted; // Use named method
            QueueViewModel.OperationStarted += _queueOperationStartedHandler;

            _queueOperationCompletedHandler = QueueViewModel_OperationCompleted; // Use named method
            QueueViewModel.OperationCompleted += _queueOperationCompletedHandler;

            _queueDownloadCompletedHandler = QueueViewModel_DownloadCompletedAndRefreshNeeded; // Use named method
            QueueViewModel.DownloadCompletedAndRefreshNeeded += _queueDownloadCompletedHandler;

            // Subscribe to SteamCMD service setup state changes directly for IsSteamCmdReady property?
            // Or rely solely on QueueViewModel's state? Let's keep the direct subscription for now.
             _steamCmdSetupStateChangedHandler = SteamCmdService_SetupStateChanged;
             _steamCmdService.SetupStateChanged += _steamCmdSetupStateChangedHandler;


            // Initialize commands
            CancelOperationCommand = new RelayCommand(ExecuteCancelOperation, CanExecuteCancelOperation);

            // Check SteamCMD status on startup (Can be done via service event now)
            // Task.Run(UpdateSteamCmdReadyStatus); // Initial check still okay
        }

        // --- Event Handlers ---

        private void QueueViewModel_OperationStarted(object? sender, EventArgs e)
        {
             SetOperationInProgress(true, "Operation started..."); // Update status
        }

        private void QueueViewModel_OperationCompleted(object? sender, EventArgs e)
        {
            // Important: Ensure this is called *after* the operation actually finishes
            // and the CancellationTokenSource is no longer needed.
             SetOperationInProgress(false, "Operation finished."); // Update status
        }

        // Handler to forward the event AND trigger local refresh
        private void QueueViewModel_DownloadCompletedAndRefreshNeeded(object? sender, EventArgs e)
        {
            Debug.WriteLine("[DownloaderVM] Received DownloadCompletedAndRefreshNeeded from QueueVM.");
            // Forward the event to the parent UI (e.g., MainViewModel) if needed
            DownloadCompletedAndRefreshNeeded?.Invoke(this, e);

            // Trigger the QueueViewModel to refresh its display based on new local mod state
             RunOnUIThread(async () =>
             {
                 await Task.Delay(100); // Small delay allows UI to settle potentially
                 Debug.WriteLine("[DownloaderVM] Triggering QueueViewModel.RefreshLocalModInfo().");
                 QueueViewModel.RefreshLocalModInfo();
             });
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(IsSteamCmdReady));
                // Refresh commands in children that depend on this state (QueueVM handles its own via its subscription)
                // QueueViewModel.RefreshCommandStates(); // Probably not needed if QueueVM reacts itself
                // Refresh this VM's commands if any depend on it directly
                 ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged(); // Cancel depends on IsOperationInProgress
            });
        }

        // --- Methods ---

        public void SetWebView(WebView2 webView)
        {
            BrowserViewModel.SetWebView(webView);
            // Initial command state update
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(IsOperationInProgress)); // Update initial state
                RefreshCommandStates(); // Initial refresh after setup
            });
        }

        // Central point to manage operation state and notify children
        private void SetOperationInProgress(bool inProgress, string statusMessage = "")
        {
            bool changed = false;

            if (inProgress && _currentOperationCts == null)
            {
                _currentOperationCts = new CancellationTokenSource();
                changed = true;
                Debug.WriteLine("[DownloaderVM] SetOperationInProgress(true)");
            }
            else if (!inProgress && _currentOperationCts != null)
            {
                // Don't cancel/dispose here if the operation isn't truly finished.
                // The operation ending should trigger OperationCompleted, which calls this method.
                // We only dispose the CTS when setting state to *not* in progress.
                var ctsToDispose = _currentOperationCts;
                _currentOperationCts = null; // Set to null *before* disposing
                ctsToDispose.Cancel(); // Ensure cancellation is requested if not already
                ctsToDispose.Dispose();
                changed = true;
                Debug.WriteLine("[DownloaderVM] SetOperationInProgress(false), CTS disposed.");
            }

            if (changed)
            {
                RunOnUIThread(() =>
                {
                     // Update this VM's property first
                    OnPropertyChanged(nameof(IsOperationInProgress));
                    // Then notify the child VM
                    QueueViewModel.IsOperationInProgress = inProgress;
                    Debug.WriteLine($"[DownloaderVM.SetOperationInProgress] Notified QueueVM.IsOperationInProgress = {inProgress}. Refreshing commands.");
                    RefreshCommandStates(); // Refresh commands on this VM and children
                });

                 if (!string.IsNullOrEmpty(statusMessage))
                    StatusBarViewModel.SetStatus(statusMessage);
                 else if (!inProgress) // Only set default 'Ready' when becoming idle
                    StatusBarViewModel.SetStatus("Ready");
            }
        }

        // Provides the token for child operations
        public CancellationToken GetCancellationToken()
        {
            // Return the current token if an operation is in progress, otherwise None.
            // If CTS is null, accessing Token throws, so handle that.
            return _currentOperationCts?.Token ?? CancellationToken.None;
        }

        private void RefreshCommandStates()
        {
            RunOnUIThread(() =>
            {
                ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
                BrowserViewModel.RefreshCommandStates();
                QueueViewModel.RefreshCommandStates(); // QueueVM refreshes its own + handler's commands
            });
        }

        // Keep or remove based on whether direct subscription is kept
        private async Task UpdateSteamCmdReadyStatus()
        {
             await _steamCmdService.CheckSetupAsync();
             // The SteamCmdService_SetupStateChanged handler will update the property and commands
        }

        private bool CanExecuteCancelOperation(object? parameter) =>
            IsOperationInProgress && _currentOperationCts != null && !_currentOperationCts.IsCancellationRequested;

        private void ExecuteCancelOperation(object? parameter)
        {
            if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
            {
                StatusBarViewModel.SetStatus("Attempting to cancel current operation...");
                Debug.WriteLine("[DownloaderVM] Requesting cancellation via CTS.");
                try
                {
                    _currentOperationCts.Cancel();
                    StatusBarViewModel.SetStatus("Cancellation requested. Operation will stop when possible.");
                    // OperationCompleted handler will call SetOperationInProgress(false) when the task finishes cancelling.
                    // Refresh CanExecute state for the cancel button itself.
                    RunOnUIThread(() => ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged());
                }
                catch (ObjectDisposedException) { Debug.WriteLine("[DownloaderVM] Tried to cancel an already disposed CTS."); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DownloaderVM] Error during cancellation request: {ex.Message}");
                    StatusBarViewModel.SetStatus("Error requesting cancellation.");
                }
            } else {
                 Debug.WriteLine("[DownloaderVM] Cancel called but no operation in progress or already cancelled.");
            }
        }

        // Cleanup method
        public void Cleanup()
        {
            Debug.WriteLine("[DownloaderVM] Cleaning up subscriptions...");

            // Unsubscribe from child events
            if (_browserStatusHandler != null) BrowserViewModel.StatusChanged -= _browserStatusHandler;
            if (_queueStatusHandler != null) QueueViewModel.StatusChanged -= _queueStatusHandler;
            if (_queueOperationStartedHandler != null) QueueViewModel.OperationStarted -= _queueOperationStartedHandler;
            if (_queueOperationCompletedHandler != null) QueueViewModel.OperationCompleted -= _queueOperationCompletedHandler;
            if (_queueDownloadCompletedHandler != null) QueueViewModel.DownloadCompletedAndRefreshNeeded -= _queueDownloadCompletedHandler;

            // Unsubscribe from service events
             if (_steamCmdSetupStateChangedHandler != null)
                 _steamCmdService.SetupStateChanged -= _steamCmdSetupStateChangedHandler;


            // Call Cleanup on child ViewModels
            BrowserViewModel?.Cleanup();
            QueueViewModel?.Cleanup(); // Ensure QueueVM cleans up its handler too

            // Dispose CTS if it exists and hasn't been disposed
            _currentOperationCts?.Cancel(); // Request cancellation
            _currentOperationCts?.Dispose();
            _currentOperationCts = null;

             // Clear handlers
            _browserStatusHandler = null;
            _queueStatusHandler = null;
            _queueOperationStartedHandler = null;
            _queueOperationCompletedHandler = null;
            _queueDownloadCompletedHandler = null;
            _steamCmdSetupStateChangedHandler = null;

             Debug.WriteLine("[DownloaderVM] Cleanup complete.");
        }
    }
}