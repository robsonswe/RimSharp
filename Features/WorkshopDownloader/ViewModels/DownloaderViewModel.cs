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
using RimSharp.Infrastructure.Workshop;
using System.Diagnostics;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Services;

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    /// <summary>
    /// Orchestrator ViewModel that coordinates the sub-components of the Workshop Downloader
    /// </summary>
    public class DownloaderViewModel : ViewModelBase
    {
        // Child ViewModels
        public BrowserViewModel BrowserViewModel { get; }
        public DownloadQueueViewModel QueueViewModel { get; }
        public StatusBarViewModel StatusBarViewModel { get; }

        // Services
        private readonly ISteamCmdService _steamCmdService;
        private readonly IDialogService _dialogService;
        private readonly IModService _modService; // Keep although not used directly, might be needed later or for context

        // Operational state
        private CancellationTokenSource? _currentOperationCts;
        public bool IsOperationInProgress => _currentOperationCts != null;
        public bool IsSteamCmdReady => _steamCmdService?.IsSetupComplete ?? false;

        // Cancel operation command
        public ICommand CancelOperationCommand { get; }

        // --- NEW: Event to forward from DownloadQueueViewModel ---
        public event EventHandler? DownloadCompletedAndRefreshNeeded;

        // --- Store named handlers for unsubscribing ---
        private EventHandler<string>? _browserStatusHandler;
        private EventHandler<string>? _queueStatusHandler;
        private EventHandler? _queueOperationStartedHandler;
        private EventHandler? _queueOperationCompletedHandler;
        private EventHandler? _queueDownloadCompletedHandler;


        public DownloaderViewModel(
            IWebNavigationService navigationService,
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService)
        {
            _modService = modService;
            _dialogService = dialogService;
            _steamCmdService = steamCmdService;

            // Initialize child ViewModels
            BrowserViewModel = new BrowserViewModel(navigationService, this);
            QueueViewModel = new DownloadQueueViewModel(
                queueService,
                modService,
                dialogService,
                updateCheckerService,
                steamCmdService,
                BrowserViewModel,
                GetCancellationToken // Pass delegate to get token
            );
            StatusBarViewModel = new StatusBarViewModel();

            // --- Subscribe to status/operation updates from child ViewModels using named handlers ---
            _browserStatusHandler = (s, message) => StatusBarViewModel.SetStatus(message);
            BrowserViewModel.StatusChanged += _browserStatusHandler;

            _queueStatusHandler = (s, message) => StatusBarViewModel.SetStatus(message);
            QueueViewModel.StatusChanged += _queueStatusHandler;

            // --- NEW: Subscribe to operation start/complete events ---
            _queueOperationStartedHandler = (s, e) => SetOperationInProgress(true, "Operation started..."); // Optionally update status
            QueueViewModel.OperationStarted += _queueOperationStartedHandler;

            _queueOperationCompletedHandler = (s, e) => SetOperationInProgress(false, "Operation finished."); // Update status
            QueueViewModel.OperationCompleted += _queueOperationCompletedHandler;

            // --- NEW: Subscribe to the DownloadCompletedAndRefreshNeeded event from QueueViewModel ---
            _queueDownloadCompletedHandler = QueueViewModel_DownloadCompletedAndRefreshNeeded;
            QueueViewModel.DownloadCompletedAndRefreshNeeded += _queueDownloadCompletedHandler;

            // Subscribe to SteamCMD service setup state changes
            _steamCmdService.SetupStateChanged += SteamCmdService_SetupStateChanged;


            // Initialize commands
            CancelOperationCommand = new RelayCommand(ExecuteCancelOperation, CanExecuteCancelOperation);

            // Check SteamCMD status on startup
            Task.Run(UpdateSteamCmdReadyStatus);
        }

        // --- Event handler to forward the event ---
        private void QueueViewModel_DownloadCompletedAndRefreshNeeded(object? sender, EventArgs e)
        {
            Debug.WriteLine("[DownloaderVM] Received DownloadCompletedAndRefreshNeeded from QueueVM. Forwarding...");
            // Forward the event to listeners of DownloaderViewModel (like MainViewModel)
            DownloadCompletedAndRefreshNeeded?.Invoke(this, e);
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(IsSteamCmdReady));
                // Refresh commands in children that depend on this state
                 QueueViewModel.RefreshCommandStates();
                 // Potentially refresh this VM's commands if any depend on it directly
                 // ((RelayCommand)SomeCommand).RaiseCanExecuteChanged();
            });
        }


        public void SetWebView(WebView2 webView)
        {
            BrowserViewModel.SetWebView(webView);

            // Initial command state update
            RunOnUIThread(() => {
                OnPropertyChanged(nameof(IsOperationInProgress));
                RefreshCommandStates(); // Initial refresh after setup
            });
        }

        // Central point to manage operation state and notify children
        private void SetOperationInProgress(bool inProgress, string statusMessage = "")
        {
            CancellationTokenSource? previousCts = _currentOperationCts;
            bool changed = false;

            if (inProgress && _currentOperationCts == null)
            {
                _currentOperationCts = new CancellationTokenSource();
                changed = true;
                Debug.WriteLine("[DownloaderVM] SetOperationInProgress(true)");

                if (!string.IsNullOrEmpty(statusMessage))
                    StatusBarViewModel.SetStatus(statusMessage);
            }
            else if (!inProgress && _currentOperationCts != null)
            {
                // Dispose the previous CTS *before* setting _currentOperationCts to null
                previousCts?.Cancel(); // Request cancellation first if not already done
                previousCts?.Dispose();
                _currentOperationCts = null;
                changed = true;
                Debug.WriteLine("[DownloaderVM] SetOperationInProgress(false)");

                if (!string.IsNullOrEmpty(statusMessage))
                    StatusBarViewModel.SetStatus(statusMessage);
                else
                    StatusBarViewModel.SetStatus("Ready"); // Default status when not busy
            }

            if (changed)
            {
                 RunOnUIThread(() => {
                    OnPropertyChanged(nameof(IsOperationInProgress));
                    QueueViewModel.IsOperationInProgress = inProgress; // Notify child VM
                    Debug.WriteLine("[DownloaderVM] IsOperationInProgress changed. Refreshing command states...");
                    RefreshCommandStates(); // Refresh commands on this VM and children
                 });
            }
             // Ensure previousCts is disposed even if state didn't change (edge case?)
             else if (!inProgress && previousCts != null && previousCts != _currentOperationCts)
             {
                 previousCts.Dispose();
             }
        }


        public CancellationToken GetCancellationToken()
        {
            // Return the current token if an operation is in progress, otherwise None.
            return _currentOperationCts?.Token ?? CancellationToken.None;
        }

        private void RefreshCommandStates()
        {
            // Ensure UI thread as commands affect UI elements
            RunOnUIThread(() =>
            {
                ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();

                // Refresh child ViewModels' commands
                BrowserViewModel.RefreshCommandStates();
                QueueViewModel.RefreshCommandStates();
            });
        }

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
                try
                {
                    _currentOperationCts.Cancel();
                    StatusBarViewModel.SetStatus("Cancellation requested. Operation will stop when possible.");
                    // Don't set IsOperationInProgress = false here. Let the operation itself handle cancellation
                    // and call the OperationCompleted event -> SetOperationInProgress(false) flow.
                }
                catch (ObjectDisposedException) { /* Already disposed, ignore */ }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during cancellation request: {ex.Message}");
                    StatusBarViewModel.SetStatus("Error requesting cancellation.");
                }
                finally
                {
                     // Re-evaluate CanExecute after cancellation attempt
                    RunOnUIThread(() => ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged());
                }
            }
        }

        // --- NEW: Cleanup method to unsubscribe events ---
        public void Cleanup()
        {
            Debug.WriteLine("[DownloaderVM] Cleaning up subscriptions...");

            // Unsubscribe from child events using stored handlers
            if (_browserStatusHandler != null)
                BrowserViewModel.StatusChanged -= _browserStatusHandler;
            if (_queueStatusHandler != null)
                QueueViewModel.StatusChanged -= _queueStatusHandler;
            if (_queueOperationStartedHandler != null)
                QueueViewModel.OperationStarted -= _queueOperationStartedHandler;
            if (_queueOperationCompletedHandler != null)
                QueueViewModel.OperationCompleted -= _queueOperationCompletedHandler;
            if (_queueDownloadCompletedHandler != null)
                QueueViewModel.DownloadCompletedAndRefreshNeeded -= _queueDownloadCompletedHandler;

            // Unsubscribe from service events
            _steamCmdService.SetupStateChanged -= SteamCmdService_SetupStateChanged;


            // Call Cleanup on child ViewModels if they have it
            BrowserViewModel.Cleanup();
            QueueViewModel.Cleanup();

             // Dispose CTS if it exists
            _currentOperationCts?.Cancel();
            _currentOperationCts?.Dispose();
            _currentOperationCts = null;
        }
    }
}