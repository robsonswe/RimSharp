using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf; // <<< CHANGED
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts; // Add this for IModService, IDialogService
using System.Net.Http; // Required if injecting HttpClient directly, better via factory if possible
using System.Collections.Generic; // Required for List
using System.Threading.Tasks;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.MyApp.Dialogs;
using System.Threading; // Required for Task
using RimSharp.Infrastructure.Workshop; // <<< ADDED for SteamCmdDownloadResult
using System.Diagnostics; // <<< For Debug.WriteLine

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    public class DownloaderViewModel : ViewModelBase
    {
        private readonly IWebNavigationService _navigationService;
        private readonly IDownloadQueueService _queueService;
        // Keep IModExtractorService interface, the implementation detail changes
        private IModExtractorService _extractorService;
        private string _statusMessage;

        private readonly IModService _modService; // To get the list of installed mods
        private readonly IDialogService _dialogService; // To show the update check dialog
        private readonly IWorkshopUpdateCheckerService _updateCheckerService; // To perform the update check
        private readonly ISteamCmdService _steamCmdService;

        private Microsoft.Web.WebView2.Wpf.WebView2 _webView;
        private CancellationTokenSource _currentOperationCts;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<DownloadItem> DownloadList => _queueService.Items;

        public bool CanGoBack => _navigationService.CanGoBack;
        public bool CanGoForward => _navigationService.CanGoForward;
        public bool IsValidModUrl => _navigationService.IsValidModUrl;
        public bool IsSteamCmdReady => _steamCmdService?.IsSetupComplete ?? false;

        // Browser Navigation Commands
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }

        // Other commands
        public ICommand AddModCommand { get; }
        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand NavigateToUrlCommand { get; }
        public ICommand CancelOperationCommand { get; }

        public DownloaderViewModel(
    IWebNavigationService navigationService,
    IDownloadQueueService queueService,
    IModService modService, // Add IModService
    IDialogService dialogService, // Add IDialogService
    IWorkshopUpdateCheckerService updateCheckerService, // Add IWorkshopUpdateCheckerService
    ISteamCmdService steamCmdService)
        {
            _navigationService = navigationService;
            _queueService = queueService;
            _modService = modService; // Store injected service
            _dialogService = dialogService; // Store injected service
            _updateCheckerService = updateCheckerService; // Store injected service
            _steamCmdService = steamCmdService;

            // Set up event handlers
            _navigationService.StatusChanged += (s, message) => StatusMessage = message;
            _navigationService.NavigationStateChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
            };
            _navigationService.ModUrlValidityChanged += (s, valid) =>
            {
                OnPropertyChanged(nameof(IsValidModUrl));
                // Need to raise CanExecuteChanged for commands depending on IsValidModUrl
                ((RelayCommand)AddModCommand).RaiseCanExecuteChanged();
            };

            _queueService.StatusChanged += (s, message) => StatusMessage = message;

            // Initialize commands
            GoBackCommand = new RelayCommand(_ => _navigationService.GoBack(), _ => CanGoBack);
            GoForwardCommand = new RelayCommand(_ => _navigationService.GoForward(), _ => CanGoForward);
            GoHomeCommand = new RelayCommand(_ => _navigationService.GoHome());

            AddModCommand = new RelayCommand(
                ExecuteAddModCommand,
                _ => IsValidModUrl); // Predicate based on property

            RemoveItemCommand = new RelayCommand(
                param => _queueService.RemoveFromQueue(param as DownloadItem));
            CheckUpdatesCommand = new RelayCommand(async _ => await ExecuteCheckUpdatesCommand(), CanExecuteCheckUpdates);
            NavigateToUrlCommand = new RelayCommand(ExecuteNavigateToUrlCommand);

            // Stub commands for future implementation
            SetupSteamCmdCommand = new RelayCommand(async _ => await ExecuteSetupSteamCmdCommand(), CanExecuteSetupSteamCmd);
            DownloadCommand = new RelayCommand(async _ => await ExecuteDownloadCommand(), CanExecuteDownload);
            CancelOperationCommand = new RelayCommand(ExecuteCancelOperation, CanExecuteCancelOperation); // <<< NEW

            _queueService.Items.CollectionChanged += (s, e) => ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged(); // Initial check

            // Re-evaluate command states when navigation changes might affect them
            _navigationService.NavigationStateChanged += (s, e) =>
            {
                ((RelayCommand)GoBackCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GoForwardCommand).RaiseCanExecuteChanged();
            };
        }



        // Change the parameter type to the WPF WebView2
        public void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            _webView = webView;
            _navigationService.SetWebView(webView);
            _extractorService = new ModExtractorService(webView);
            ((RelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
        }
        private async Task UpdateSteamCmdReadyStatus()
        {
            await _steamCmdService.CheckSetupAsync(); // Ensure service state is fresh
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(IsSteamCmdReady));
                // Re-evaluate commands that depend on SteamCMD status
                ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
            });
        }

        private bool CanExecuteSetupSteamCmd(object parameter)
        {
            // Can always try to set up, unless an operation is already running
            return _currentOperationCts == null;
        }

        private async Task ExecuteSetupSteamCmdCommand()
        {
            if (_currentOperationCts != null) return; // Prevent concurrent operations

            ProgressDialogViewModel? progressDialog = null;
            _currentOperationCts = new CancellationTokenSource();
            ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged(); // Disable setup btn
            ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged(); // Disable download btn
            ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged(); // Enable cancel btn


            try
            {
                progressDialog = _dialogService.ShowProgressDialog(
                    "SteamCMD Setup",
                    "Starting setup...",
                    true); // Can Cancel

                // Link progress dialog cancellation to our token
                progressDialog.Cancelled += (s, e) => _currentOperationCts?.Cancel();

                var progressReporter = new Progress<string>(message =>
                {
                    if (progressDialog != null) progressDialog.Message = message;
                    StatusMessage = message; // Update main status bar too
                });

                StatusMessage = "Running SteamCMD Setup...";
                bool success = await _steamCmdService.SetupAsync(progressReporter, _currentOperationCts.Token);

                if (success)
                {
                    progressDialog?.Complete("Setup completed successfully!");
                    StatusMessage = "SteamCMD setup successful.";
                    _dialogService.ShowInformation("Setup Complete", "SteamCMD has been set up successfully.");
                }
                else if (!_currentOperationCts.IsCancellationRequested)
                {
                    // Failure message already shown by service or progress reporter
                    progressDialog?.Cancel("Setup failed. See previous messages for details.");
                    StatusMessage = "SteamCMD setup failed.";
                }
                else
                {
                    // Cancellation message
                    progressDialog?.Cancel("Setup cancelled by user.");
                    StatusMessage = "SteamCMD setup cancelled.";
                }
            }
            catch (OperationCanceledException)
            {
                progressDialog?.Cancel("Setup cancelled by user.");
                StatusMessage = "SteamCMD setup cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during SteamCMD setup: {ex.Message}";
                progressDialog?.Cancel($"Error: {ex.Message}");
                _dialogService.ShowError("Setup Error", $"An unexpected error occurred during setup: {ex.Message}");
            }
            finally
            {
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                await UpdateSteamCmdReadyStatus(); // Refresh IsSteamCmdReady property
                RunOnUIThread(() =>
                { // Re-enable buttons
                    ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
                });
                progressDialog?.ForceClose(); // Ensure dialog closes if not already
            }
        }


        private bool CanExecuteDownload(object parameter)
        {
            // Need items in the queue AND SteamCMD setup AND no operation running
            return DownloadList.Any() && IsSteamCmdReady && _currentOperationCts == null;
        }

               private async Task ExecuteDownloadCommand()
        {
            if (_currentOperationCts != null) return; // Already running

            // Check SteamCMD readiness (same as before)
            if (!IsSteamCmdReady)
            {
                var result = _dialogService.ShowConfirmation("SteamCMD Not Ready", "SteamCMD setup is not detected or incomplete. Do you want to run the setup now?", showCancel: true);
                if (result == MessageDialogResult.OK || result == MessageDialogResult.Yes)
                {
                    await ExecuteSetupSteamCmdCommand(); // Run setup
                    // Don't proceed with download yet, let user click download again after setup
                    return;
                }
                else
                {
                    StatusMessage = "Download cancelled: SteamCMD setup required.";
                    return;
                }
            }

            var itemsToDownload = DownloadList.ToList(); // Snapshot the queue
            if (!itemsToDownload.Any())
            {
                StatusMessage = "Download queue is empty.";
                return;
            }

            _currentOperationCts = new CancellationTokenSource();
             RunOnUIThread(() => // Disable buttons, enable cancel
             {
                 ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                 ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                 ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
             });

            SteamCmdDownloadResult? downloadResult = null;
            bool validateDownloads = false; // Or get from settings/UI if needed

            try
            {
                StatusMessage = $"Starting download of {itemsToDownload.Count} mod(s). Please observe the SteamCMD window...";

                // --- Execute SteamCMD ---
                // No progress dialog here
                downloadResult = await _steamCmdService.DownloadModsAsync(
                    itemsToDownload,
                    validateDownloads,
                   // true, // showWindow is now implicit/default in the implementation
                    _currentOperationCts.Token);

                StatusMessage = "SteamCMD process finished. Processing results...";
                Debug.WriteLine($"SteamCMD finished. Overall Success: {downloadResult.OverallSuccess}, Exit Code: {downloadResult.ExitCode}");
                Debug.WriteLine($"Succeeded: {downloadResult.SucceededItems.Count}, Failed: {downloadResult.FailedItems.Count}");

                // --- Process Results ---
                int successCount = 0;
                int failCount = 0;

                if (downloadResult.SucceededItems.Any())
                {
                    StatusMessage = $"Creating timestamp files for {downloadResult.SucceededItems.Count} downloaded mod(s)...";
                    foreach (var successItem in downloadResult.SucceededItems)
                    {
                        try
                        {
                            // Call ModService to create the files
                            await _modService.CreateTimestampFilesAsync(
                                successItem.SteamId,
                                successItem.PublishDate, // Pass the stored PublishDate
                                successItem.StandardDate // Pass the stored StandardDate
                            );
                            Debug.WriteLine($"Timestamp files task created for {successItem.Name} ({successItem.SteamId})");
                            successCount++;

                            // Remove from the queue ON THE UI THREAD
                            RunOnUIThread(() => _queueService.RemoveFromQueue(successItem));
                        }
                        catch (Exception ex)
                        {
                             Debug.WriteLine($"Error creating timestamp files or removing item {successItem.Name} ({successItem.SteamId}): {ex.Message}");
                            // Keep the item in the failed count/list even if timestamp writing failed? Yes, download succeeded.
                            // The item *was* removed from the queue if successful.
                            // Maybe add a specific error message?
                            failCount++; // Treat timestamp error as a failure in the *overall* operation sense for reporting
                        }
                    }
                }

                // Failed items are already determined by SteamCmdDownloader (those not in SucceededItems)
                failCount = downloadResult.FailedItems.Count; // Use the count from the result

                // --- Final Reporting ---
                 string summary;
                 if (successCount > 0 && failCount == 0)
                 {
                    summary = $"Successfully downloaded {successCount} mod(s).";
                    StatusMessage = summary;
                    _dialogService.ShowInformation("Download Complete", summary);
                 }
                 else if (successCount > 0 && failCount > 0)
                 {
                     summary = $"Download finished. {successCount} succeeded, {failCount} failed (failed items remain in queue).";
                     StatusMessage = summary;
                     _dialogService.ShowWarning("Download Partially Complete", summary);
                 }
                 else if (successCount == 0 && failCount > 0)
                 {
                     summary = $"Download failed. {failCount} item(s) could not be downloaded and remain in the queue. Check SteamCMD log for details.";
                      StatusMessage = summary;
                     _dialogService.ShowError("Download Failed", summary);
                 }
                 else if (downloadResult.ExitCode != 0) // No successes, no explicit failures parsed, but exit code was bad
                 {
                     summary = $"SteamCMD process exited with code {downloadResult.ExitCode}. No items were successfully processed. Check log.";
                      StatusMessage = summary;
                     _dialogService.ShowError("Download Failed", summary);
                 }
                 else // Should not happen if list was not empty, but handle defensively
                 {
                     summary = "Download process finished, but outcome is unclear. Check SteamCMD log.";
                     StatusMessage = summary;
                      _dialogService.ShowWarning("Download Finished", summary);
                 }

                 // Log details for debugging
                 Debug.WriteLine($"Download Summary: {summary}");
                 if(downloadResult?.LogMessages.Any() ?? false)
                 {
                    Debug.WriteLine("--- SteamCMD Log ---");
                    // foreach(var logLine in downloadResult.LogMessages) { Debug.WriteLine(logLine); } // Can be very verbose
                    Debug.WriteLine($"({downloadResult.LogMessages.Count} lines logged - see SteamCmdDownloader log file)");
                    Debug.WriteLine("--- End Log ---");
                 }

            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download operation cancelled by user.";
                _dialogService.ShowInformation("Cancelled", StatusMessage);
                // Items remain in queue
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during download process: {ex.Message}";
                Debug.WriteLine($"[ExecuteDownloadCommand] Exception: {ex}");
                _dialogService.ShowError("Download Error", $"An unexpected error occurred: {ex.Message}");
                // Items remain in queue
            }
            finally
            {
                var cts = _currentOperationCts; // Capture before nulling
                _currentOperationCts = null;
                cts?.Dispose();

                RunOnUIThread(() => // Re-enable buttons
                {
                    ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
                });
            }
        }



               private async void ExecuteAddModCommand(object parameter)
        {
            if (_extractorService == null)
            {
                StatusMessage = "Error: Browser component not fully initialized.";
                return;
            }
            StatusMessage = "Extracting mod info...";
            ModInfoDto modInfo = null; // Initialize
            try
            {
                modInfo = await _extractorService.ExtractFullModInfo();
                if (modInfo != null)
                {
                    // AddToQueue handles status update internally
                    if (!_queueService.AddToQueue(modInfo)) { /* Status already set */ }
                }
                else
                {
                    StatusMessage = "Could not extract mod info from the current page.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding mod: {ex.Message}";
                Debug.WriteLine($"Error adding mod: {ex}");
            }
            finally
            {
                // Optionally clear status after a delay if successful?
            }
        }

        private void ExecuteNavigateToUrlCommand(object url)
        {
            Console.WriteLine($"Command received: {url} (type: {url?.GetType().Name})");

            if (url is string urlString && !string.IsNullOrEmpty(urlString))
            {
                try
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.Navigate(urlString);
                    }
                    else if (_webView != null)
                    {
                        _webView.Source = new Uri(urlString);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error navigating to URL: {ex.Message}";
                }
            }
        }

        private bool CanExecuteCheckUpdates(object parameter)
        {
            // Enable if the mod service has loaded some mods
            // More specific: enable if there are workshop mods loaded
            try
            {
                return _modService?.GetLoadedMods()
                                  .Any(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _))
                       ?? false;
            }
            catch
            {
                return false; // Handle potential errors during GetLoadedMods
            }
        }


        private async Task ExecuteCheckUpdatesCommand()
        {
            StatusMessage = "Gathering installed workshop mods...";
            List<ModItem> workshopMods;
            try
            {
                // Get all workshop mods with valid Steam IDs
                workshopMods = _modService.GetLoadedMods()
                   .Where(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _))
                   .OrderBy(m => m.Name)
                   .ToList();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading mod list: {ex.Message}";
                Console.WriteLine($"Error getting mods for update check: {ex}");
                return;
            }

            if (!workshopMods.Any())
            {
                StatusMessage = "No installed Steam Workshop mods found to check.";
                _dialogService.ShowInformation("Check Updates", "No installed Steam Workshop mods were found in your mods folder.");
                return;
            }

            // Create and show the selection dialog
            var dialogViewModel = new UpdateCheckDialogViewModel(workshopMods);
            var dialogResult = _dialogService.ShowUpdateCheckDialog(dialogViewModel);

            if (dialogResult != UpdateCheckDialogResult.CheckUpdates)
            {
                StatusMessage = "Update check cancelled.";
                return;
            }

            var selectedMods = dialogViewModel.GetSelectedMods().ToList();
            if (!selectedMods.Any())
            {
                StatusMessage = "No mods were selected for update check.";
                return;
            }

            StatusMessage = $"Checking {selectedMods.Count} mod(s) for updates...";

            // Create and show progress dialog
            ProgressDialogViewModel progressDialog = null;
            IProgress<(int current, int total, string modName)> progress = null;

            try
            {
                progressDialog = _dialogService.ShowProgressDialog(
                    "Checking for Updates",
                    "Preparing to check mods...",
                    true);

                progress = new Progress<(int current, int total, string modName)>(update =>
                {
                    progressDialog.Message = $"Checking {update.modName}... ({update.current}/{update.total})";
                    progressDialog.Progress = (int)((double)update.current / update.total * 100);

                    // Update status message in main window too
                    StatusMessage = $"Checking {update.modName} ({update.current} of {update.total})...";
                });

                // Perform the update check
                var updateResult = await _updateCheckerService.CheckForUpdatesAsync(selectedMods, progress);

                // Close progress dialog
                progressDialog.Complete();
                progressDialog = null;

                // Report results
                string summary = $"Update check complete. Checked: {updateResult.ModsChecked}. Updates found: {updateResult.UpdatesFound}.";
                if (updateResult.ErrorsEncountered > 0)
                {
                    summary += $" Errors: {updateResult.ErrorsEncountered}.";

                    // Show first 3 errors if there are many
                    var errorSample = updateResult.ErrorMessages.Take(3).ToList();
                    var errorMessage = string.Join("\n", errorSample);

                    if (updateResult.ErrorMessages.Count > 3)
                    {
                        errorMessage += $"\n(and {updateResult.ErrorsEncountered - 3} more errors...)";
                    }

                    _dialogService.ShowWarning("Update Check Errors",
                        $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\n\n{errorMessage}");
                }

                StatusMessage = summary;

                if (updateResult.UpdatesFound > 0)
                {
                    _dialogService.ShowInformation("Updates Found",
                        $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
                }
                else
                {
                    _dialogService.ShowInformation("No Updates Found",
                        "All selected mods are up to date.");
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled the operation
                progressDialog?.Cancel();
                StatusMessage = "Update check was cancelled.";
                _dialogService.ShowInformation("Cancelled", "The update check was cancelled.");
            }
            catch (Exception ex)
            {
                progressDialog?.Cancel();
                StatusMessage = $"An error occurred during the update check process: {ex.Message}";
                Console.WriteLine($"Error executing update check: {ex}");
                _dialogService.ShowError("Update Check Failed", $"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                progressDialog?.Cancel();
            }
        }

        private bool CanExecuteCancelOperation(object parameter)
        {
            // Enable only when an operation (Setup, Download, CheckUpdates) is running
            return _currentOperationCts != null && !_currentOperationCts.IsCancellationRequested;
        }

        private void ExecuteCancelOperation(object parameter)
        {
            if (CanExecuteCancelOperation(parameter))
            {
                StatusMessage = "Attempting to cancel current operation...";
                try
                {
                    _currentOperationCts?.Cancel();

                    // Note: Cancelling SteamCMD download might require manual window closing.
                    // The log parsing will still happen when the process eventually exits.
                    // Cancellation is most effective *before* SteamCMD starts or during API calls.
                     StatusMessage = "Cancellation requested. Operation will stop when possible.";
                }
                catch (ObjectDisposedException) { /* Already disposed, ignore */ }
                catch (Exception ex)
                {
                     Debug.WriteLine($"Error during cancellation request: {ex.Message}");
                     StatusMessage = "Error requesting cancellation.";
                }
                finally
                {
                     RunOnUIThread(() => ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged()); // Disable button after requesting
                }
            }
        }


    }
}
