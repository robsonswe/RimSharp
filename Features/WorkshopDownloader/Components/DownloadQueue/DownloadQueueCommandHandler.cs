#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck; // Ensure this is the correct namespace
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Infrastructure.Workshop;
using RimSharp.MyApp.Dialogs; // Ensure this is the correct namespace
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    /// <summary>
    /// Handles the execution logic for commands related to the download queue.
    /// Separates command logic from the ViewModel's state management and UI updates.
    /// </summary>
    public class DownloadQueueCommandHandler
    {
        private readonly IDownloadQueueService _queueService;
        private readonly IModService _modService;
        private readonly IDialogService _dialogService;
        private readonly IWorkshopUpdateCheckerService _updateCheckerService;
        private readonly ISteamCmdService _steamCmdService;
        private readonly BrowserViewModel _browserViewModel;
        private readonly Func<CancellationToken> _getCancellationToken;
        private readonly IModListManager _modListManager;
        private readonly ModInfoEnricher _modInfoEnricher; // Gets passed in
        private EventHandler<string>? _queueServiceStatusHandler;

        // State properties updated by the owning ViewModel
        public bool IsSteamCmdReady { get; private set; }
        public bool IsOperationInProgress { get; set; } // Allows ViewModel to control this

        // Commands (defined here, exposed by ViewModel)
        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand AddModCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand NavigateToUrlCommand { get; }
        public ICommand RemoveItemsCommand { get; }

        // Events (raised here, subscribed to by ViewModel)
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationStarted;
        public event EventHandler? OperationCompleted;
        public event EventHandler? DownloadCompletedAndRefreshNeeded;
        public event EventHandler? SteamCmdReadyStatusChanged; // Notify VM when this changes internally

        public DownloadQueueCommandHandler(
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService,
            BrowserViewModel browserViewModel,
            Func<CancellationToken> getCancellationToken,
            IModListManager modListManager,
            ModInfoEnricher modInfoEnricher) // Inject the enricher
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _updateCheckerService = updateCheckerService ?? throw new ArgumentNullException(nameof(updateCheckerService));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _getCancellationToken = getCancellationToken ?? throw new ArgumentNullException(nameof(getCancellationToken));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _modInfoEnricher = modInfoEnricher ?? throw new ArgumentNullException(nameof(modInfoEnricher)); // Store the enricher

            // Initialize commands
            SetupSteamCmdCommand = new AsyncRelayCommand(ExecuteSetupSteamCmdCommand, CanExecuteSetupSteamCmd);
            CheckUpdatesCommand = new AsyncRelayCommand(ExecuteCheckUpdatesCommand, CanExecuteCheckUpdates);
            DownloadCommand = new AsyncRelayCommand(ExecuteDownloadCommand, CanExecuteDownload); // Use method
            AddModCommand = new AsyncRelayCommand(ExecuteAddModCommand, CanExecuteAddMod); // Use method
            RemoveItemCommand = new RelayCommand(ExecuteRemoveItemCommand, CanExecuteRemoveItem); // Use method
            NavigateToUrlCommand = new RelayCommand(ExecuteNavigateToUrlCommand, CanExecuteNavigateToUrl); // Use method
            RemoveItemsCommand = new RelayCommand(ExecuteRemoveItemsCommand, CanExecuteRemoveItems); // Use method

            // Subscribe to queue service status for forwarding
            // Use a named handler to allow unsubscribing
            _queueServiceStatusHandler = (s, msg) => StatusChanged?.Invoke(this, msg);
            _queueService.StatusChanged += _queueServiceStatusHandler;

            // Initial check (fire and forget is okay here)
            _ = UpdateSteamCmdReadyStatus();
        }

        // --- CanExecute Methods ---
        // These methods encapsulate the logic previously in lambdas or separate methods in the ViewModel.
        // They depend on the state properties (IsOperationInProgress, IsSteamCmdReady) managed by this handler.

        private bool CanExecuteDownload() => _queueService.Items.Any() && IsSteamCmdReady && !IsOperationInProgress;
        private bool CanExecuteAddMod() => _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress;
        private bool CanExecuteSetupSteamCmd() => !IsOperationInProgress;
        private bool CanExecuteRemoveItem(object? parameter) => !IsOperationInProgress && parameter is DownloadItem;
        private bool CanExecuteRemoveItems(object? parameter) => !IsOperationInProgress && parameter is System.Collections.IList list && list.Count > 0;
        private bool CanExecuteNavigateToUrl(object? parameter) => !IsOperationInProgress && parameter is string url && !string.IsNullOrWhiteSpace(url);

        private bool CanExecuteCheckUpdates()
        {
            // Keep the try-catch as mod loading could potentially fail
            try
            {
                return !IsOperationInProgress && (_modService?.GetLoadedMods().Any(m =>
                    !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _)) ?? false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandler] Error checking CanExecuteCheckUpdates: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error checking mod list: {ex.Message}");
                return false;
            }
        }

        // Method called by ViewModel to force re-evaluation of CanExecute
        public void RefreshCommandStates()
        {
            // Use Task.Run to ensure it doesn't block the UI thread if called synchronously,
            // though Commands usually handle dispatching correctly. Better safe than sorry.
            // Or rely on ViewModel's RunOnUIThread if calling this from there.
             System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
             {
                ((AsyncRelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)AddModCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RemoveItemCommand).RaiseCanExecuteChanged();
                ((RelayCommand)NavigateToUrlCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RemoveItemsCommand).RaiseCanExecuteChanged();
             });
        }

        // Helper to update internal state and notify ViewModel
        public async Task UpdateSteamCmdReadyStatus()
        {
            await _steamCmdService.CheckSetupAsync();
            bool previousState = IsSteamCmdReady;
            IsSteamCmdReady = _steamCmdService.IsSetupComplete;
            if (IsSteamCmdReady != previousState)
            {
                SteamCmdReadyStatusChanged?.Invoke(this, EventArgs.Empty); // Notify VM
                RefreshCommandStates(); // Update command states based on new readiness
            }
        }


        // --- Command Execute Methods (Copied and adapted from original ViewModel) ---
        // Important changes:
        // - Use injected services (_dialogService, _steamCmdService, etc.)
        // - Raise OperationStarted/OperationCompleted events.
        // - Raise StatusChanged event for user feedback.
        // - Get CancellationToken using _getCancellationToken().
        // - Use _modInfoEnricher where enrichment is needed.
        // - Raise DownloadCompletedAndRefreshNeeded event.

        private async Task ExecuteSetupSteamCmdCommand(CancellationToken ignoredToken)
        {
            // Guard clause - already checked by CanExecute but good practice
            if (IsOperationInProgress) return;
            CancellationToken token = _getCancellationToken();
            if (token.IsCancellationRequested) return;

            // Set state via event
            OperationStarted?.Invoke(this, EventArgs.Empty);
            ProgressDialogViewModel? progressDialog = null;
            try
            {
                // Use the injected dialog service
                progressDialog = _dialogService.ShowProgressDialog("SteamCMD Setup", "Starting setup...", true, true, CancellationTokenSource.CreateLinkedTokenSource(token)); // Pass linked CTS

                // Link dialog cancellation to our token (already handled by passing CTS)
                 progressDialog.Cancelled += (s, e) =>
                 {
                     // Optional: Log cancellation if needed, token handles the rest
                     StatusChanged?.Invoke(this, "Setup cancelled via dialog.");
                 };

                var progressReporter = new Progress<string>(message =>
                {
                    if (progressDialog != null) progressDialog.Message = message;
                    StatusChanged?.Invoke(this, message); // Update status bar
                });

                StatusChanged?.Invoke(this, "Running SteamCMD Setup...");
                bool success = await _steamCmdService.SetupAsync(progressReporter, token); // Pass the actual token

                if (token.IsCancellationRequested)
                {
                if (progressDialog != null) progressDialog.Message = "Setup cancelled.";
                    StatusChanged?.Invoke(this, "SteamCMD setup cancelled.");
                }
                else if (success)
                {
                    progressDialog?.CompleteOperation("Setup completed successfully!");
                    StatusChanged?.Invoke(this, "SteamCMD setup successful.");
                    _dialogService.ShowInformation("Setup Complete", "SteamCMD has been set up successfully.");
                }
                else
                {
                    // Failure message likely shown by service, keep dialog message brief
                    if (progressDialog != null) progressDialog.Message = "Setup failed. See log/messages.";
                    StatusChanged?.Invoke(this, "SteamCMD setup failed.");
                    _dialogService.ShowError("Setup Failed", "SteamCMD setup failed. Please check the logs or status messages for details.");
                }
            }
            catch (OperationCanceledException)
            {
                // Catch cancellation triggered by the token
                if (progressDialog != null) progressDialog.Message = "Setup cancelled.";
                StatusChanged?.Invoke(this, "SteamCMD setup cancelled.");
                // No need for dialog here, cancellation is expected
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during SteamCMD setup: {ex.Message}");
                if (progressDialog != null) progressDialog.Message = $"Error: {ex.Message}";
                _dialogService.ShowError("Setup Error", $"An unexpected error occurred during setup: {ex.Message}");
                Debug.WriteLine($"[ExecuteSetupSteamCmdCommand] Error: {ex}");
            }
            finally
            {
                await UpdateSteamCmdReadyStatus(); // Refresh IsSteamCmdReady property
                progressDialog?.ForceClose(); // Ensure dialog is closed if not already
                OperationCompleted?.Invoke(this, EventArgs.Empty); // Notify operation finished
            }
        }

        private async Task ExecuteDownloadCommand(CancellationToken ignoredToken)
        {
            if (IsOperationInProgress || !_queueService.Items.Any()) return;
            CancellationToken token = _getCancellationToken();
            if (token.IsCancellationRequested) return;

            // Re-check SteamCMD readiness just before execution
            await UpdateSteamCmdReadyStatus();
            if (!IsSteamCmdReady)
            {
                var result = _dialogService.ShowConfirmation("SteamCMD Not Ready", "SteamCMD setup is not detected or incomplete. Do you want to run the setup now?", showCancel: true);
                if (result == MessageDialogResult.OK || result == MessageDialogResult.Yes)
                {
                     // Ideally, the parent VM would handle chaining this.
                     // For now, just prompt the user to click the setup button.
                     StatusChanged?.Invoke(this, "Please run 'Setup SteamCMD' first.");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Download cancelled: SteamCMD setup required.");
                }
                return; // Exit download attempt
            }

            OperationStarted?.Invoke(this, EventArgs.Empty);

            var itemsToDownload = _queueService.Items.ToList(); // Snapshot the queue
            SteamCmdDownloadResult? downloadResult = null;
            bool refreshIsNeeded = false;
            // bool localInfoRefreshNeeded = false; // We will re-enrich regardless

            try
            {
                StatusChanged?.Invoke(this, $"Starting download of {itemsToDownload.Count} mod(s). Please observe the SteamCMD window...");

                // Execute SteamCMD download
                downloadResult = await _steamCmdService.DownloadModsAsync(itemsToDownload, false, token); // Pass actual token

                if (token.IsCancellationRequested) throw new OperationCanceledException();

                StatusChanged?.Invoke(this, "SteamCMD process finished. Processing results...");
                Debug.WriteLine($"[CommandHandler] SteamCMD finished. Overall Success: {downloadResult.OverallSuccess}, Exit Code: {downloadResult.ExitCode}");

                int successCount = 0;
                int failCount = downloadResult.FailedItems.Count; // Start with failed count from result

                // Process successfully downloaded items (create timestamps, remove from queue)
                if (downloadResult.SucceededItems.Any())
                {
                    StatusChanged?.Invoke(this, $"Processing {downloadResult.SucceededItems.Count} downloaded mod(s)...");
                    foreach (var successItem in downloadResult.SucceededItems)
                    {
                        if (token.IsCancellationRequested) throw new OperationCanceledException(); // Check before each item

                        try
                        {
                            // Call ModService to create timestamp files
                            await _modService.CreateTimestampFilesAsync(
                                successItem.SteamId,
                                successItem.PublishDate,
                                successItem.StandardDate);

                            successCount++;
                            refreshIsNeeded = true; // Need parent refresh
                            // Remove from the queue (ViewModel will handle UI update via CollectionChanged)
                             _queueService.RemoveFromQueue(successItem); // Assuming this happens on UI thread or is safe
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException) // Don't treat cancellation as a processing error
                        {
                            Debug.WriteLine($"[CommandHandler] Error post-processing success item {successItem.Name} ({successItem.SteamId}): {ex.Message}");
                            failCount++; // Count this as failure if post-processing fails
                            StatusChanged?.Invoke(this, $"Error processing {successItem.Name}. It remains in the queue.");
                            // Item remains in queue if post-processing fails
                        }
                    }
                }

                // --- Final Reporting (Same logic as before) ---
                string summary;
                if (token.IsCancellationRequested)
                {
                    summary = $"Download cancelled by user. {successCount} items processed before cancellation.";
                    StatusChanged?.Invoke(this, summary);
                    _dialogService.ShowInformation("Download Cancelled", summary);
                }
                else if (successCount > 0 && failCount == 0)
                {
                    summary = $"Successfully downloaded and processed {successCount} mod(s).";
                    StatusChanged?.Invoke(this, summary);
                    _dialogService.ShowInformation("Download Complete", summary);
                }
                else if (successCount > 0 && failCount > 0)
                {
                    summary = $"Download finished. {successCount} succeeded, {failCount} failed (failed items remain in queue or had post-processing errors).";
                    StatusChanged?.Invoke(this, summary);
                    _dialogService.ShowWarning("Download Partially Complete", summary);
                }
                else if (successCount == 0 && failCount > 0)
                {
                    summary = $"Download failed. {failCount} item(s) could not be downloaded or processed. Check SteamCMD log and errors.";
                    StatusChanged?.Invoke(this, summary);
                    _dialogService.ShowError("Download Failed", summary);
                }
                else if (downloadResult.ExitCode != 0) // No successes, no explicit failures parsed, but exit code was bad
                {
                    summary = $"SteamCMD process failed (Exit Code {downloadResult.ExitCode}). No items successfully processed. Check log.";
                    StatusChanged?.Invoke(this, summary);
                    _dialogService.ShowError("Download Failed", summary);
                }
                else // Should not happen if list was not empty and no cancellation, but handle defensively
                {
                    summary = "Download process finished, but outcome is unclear. Check SteamCMD log and queue.";
                    StatusChanged?.Invoke(this, summary);
                    _dialogService.ShowWarning("Download Finished", summary);
                }

                // --- Trigger Refresh Actions ---
                // Always re-enrich the remaining items after a download attempt.
                Debug.WriteLine("[CommandHandler] Download attempt finished, re-enriching remaining queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);


                if (refreshIsNeeded && !token.IsCancellationRequested)
                {
                    Debug.WriteLine("[CommandHandler] Download completed with successes, requesting parent refresh.");
                    DownloadCompletedAndRefreshNeeded?.Invoke(this, EventArgs.Empty);
                    // The event handler in DownloaderViewModel will trigger QueueViewModel.RefreshLocalModInfo() -> _modInfoEnricher
                }

            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Download operation cancelled by user.");
                 _dialogService.ShowInformation("Cancelled", "Download operation cancelled. Partially downloaded items may exist but were not fully processed.");
                 // Even on cancellation, re-enrich to reflect any partial successes before cancel took effect
                 Debug.WriteLine("[CommandHandler] Download cancelled, re-enriching queue items.");
                 _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during download process: {ex.Message}");
                Debug.WriteLine($"[ExecuteDownloadCommand] Exception: {ex}");
                _dialogService.ShowError("Download Error", $"An unexpected error occurred during the download: {ex.Message}");
                // On error, re-enrich to ensure queue reflects current known state
                Debug.WriteLine("[CommandHandler] Download error occurred, re-enriching queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
            }
            finally
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty); // Notify parent operation finished
            }
        }

        private async Task ExecuteAddModCommand(CancellationToken ignoredToken)
        {
             if (IsOperationInProgress || !CanExecuteAddMod()) return; // Use CanExecute method
             CancellationToken token = _getCancellationToken();
             if (token.IsCancellationRequested) return;

             StatusChanged?.Invoke(this, "Extracting mod info from browser...");
             // No OperationStarted/Completed needed for this quick action unless extraction is slow
             try
             {
                 // Call method on BrowserViewModel to get info
                 ModInfoDto? modInfo = await _browserViewModel.GetCurrentModInfoAsync(token); // Pass actual token

                 if (token.IsCancellationRequested)
                 {
                     StatusChanged?.Invoke(this, "Add mod cancelled.");
                     return;
                 }

                 if (modInfo != null)
                 {
                      // AddToQueue handles status update on failure (e.g., already exists)
                      // The ViewModel's CollectionChanged handler will trigger enrichment.
                     if (_queueService.AddToQueue(modInfo))
                     {
                        // Success message handled by queue service StatusChanged event if implemented there
                        // StatusChanged?.Invoke(this, $"Added '{modInfo.Name}' to queue."); // Or keep it here
                     }
                     // else: Failure message handled by queue service StatusChanged event
                 }
                 else
                 {
                     StatusChanged?.Invoke(this, "Could not extract mod info from the current page.");
                     // Optionally show a dialog if this is unexpected
                      _dialogService.ShowWarning("Extraction Failed", "Could not get mod details from the current page. Ensure it's a valid Steam Workshop item page.");
                 }
             }
             catch (OperationCanceledException)
             {
                 StatusChanged?.Invoke(this, "Add mod cancelled.");
             }
             catch (Exception ex)
             {
                 StatusChanged?.Invoke(this, $"Error adding mod: {ex.Message}");
                 Debug.WriteLine($"[ExecuteAddModCommand] Error: {ex}");
                 _dialogService.ShowError("Add Mod Error", $"An error occurred while trying to add the mod: {ex.Message}");
             }
        }

        private void ExecuteRemoveItemCommand(object? parameter)
        {
            if (IsOperationInProgress) return;

            if (parameter is DownloadItem item)
            {
                // Ask for confirmation
                var result = _dialogService.ShowConfirmation("Remove from Queue",
                    $"Are you sure you want to remove '{item.Name}' from the queue?",
                    showCancel: true);

                if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

                // Proceed with removal (QueueService should raise StatusChanged)
                if (_queueService.RemoveFromQueue(item))
                {
                    // Confirmation dialog
                    _dialogService.ShowInformation("Item Removed", $"Removed '{item.Name}' from the queue.");
                    // Status is likely already updated by QueueService, but can add one here too.
                    // StatusChanged?.Invoke(this, $"Removed '{item.Name}' from queue.");
                }
                // else: Failure is handled by QueueService status/logging
            }
        }

        private void ExecuteNavigateToUrlCommand(object? url)
        {
            if (IsOperationInProgress) return;

            if (url is string urlString && !string.IsNullOrEmpty(urlString))
            {
                // Use the BrowserViewModel's navigation command
                try
                {
                    Debug.WriteLine($"[CommandHandler] Requesting navigation to: {urlString}");
                    // Check if the command can execute (it checks !IsOperationInProgress internally)
                    if (_browserViewModel.NavigateToUrlCommand.CanExecute(urlString))
                    {
                        _browserViewModel.NavigateToUrlCommand.Execute(urlString);
                        StatusChanged?.Invoke(this, $"Navigating browser to: {urlString}");
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "Browser cannot navigate now (possibly busy).");
                        Debug.WriteLine($"[CommandHandler] BrowserViewModel.NavigateToUrlCommand cannot execute for {urlString}");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Failed to initiate browser navigation: {ex.Message}");
                    _dialogService.ShowError("Navigation Error", $"Could not navigate the internal browser: {ex.Message}");
                    Debug.WriteLine($"[CommandHandler] Error executing NavigateToUrlCommand via BrowserVM: {ex}");
                }
            }
            else
            {
                StatusChanged?.Invoke(this, "Invalid URL provided for navigation.");
            }
        }

        private void ExecuteRemoveItemsCommand(object? parameter)
        {
             if (IsOperationInProgress) return; // Don't modify queue during other operations

             // Cast parameter to IList and extract DownloadItems
             if (parameter is System.Collections.IList selectedItems && selectedItems.Count > 0)
             {
                 var items = selectedItems.Cast<DownloadItem>().ToList();
                 if (items.Count == 0) return;

                 // Prepare confirmation message
                 string confirmMessage = items.Count == 1
                     ? $"Are you sure you want to remove '{items[0].Name}' from the queue?"
                     : $"Are you sure you want to remove {items.Count} items from the queue?";

                 var result = _dialogService.ShowConfirmation("Remove from Queue", confirmMessage, showCancel: true);
                 if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

                 // Remove confirmed, proceed with removal
                 int removedCount = 0;
                 var removedNames = new List<string>();

                 // Batch remove if service supports it? Otherwise loop.
                 foreach (var item in items)
                 {
                      // Queue service handles status/logging for individual failures
                     if (item.Name != null && _queueService.RemoveFromQueue(item))
                     {
                         removedCount++;
                         removedNames.Add(item.Name);
                     }
                 }

                 // Show summary message
                 if (removedCount > 0)
                 {
                     string message = removedCount == 1
                         ? $"Removed '{removedNames[0]}' from the queue."
                         : $"Removed {removedCount} items from the queue.";
                         // Optionally list names if desired for many items:
                         //: $"Removed {removedCount} items from the queue:\n\n• " + string.Join("\n• ", removedNames);

                     _dialogService.ShowInformation("Items Removed", message);
                     StatusChanged?.Invoke(this, $"Removed {removedCount} item(s) from queue.");
                 }
                 // else: No items removed (maybe they were already gone?) or all failed. Status handled by QueueService.
             }
        }

        private async Task ExecuteCheckUpdatesCommand(CancellationToken ignoredToken)
        {
             if (IsOperationInProgress) return;

             // Use linked source to allow cancellation from parent OR dialog
             using var cts = CancellationTokenSource.CreateLinkedTokenSource(_getCancellationToken());
             var token = cts.Token;

             if (token.IsCancellationRequested) return;

             List<ModItem> workshopMods;
             try
             {
                 StatusChanged?.Invoke(this, "Gathering installed workshop mods...");
                 // Refresh local mod info just before check, ensuring IModService is up-to-date
                 await _modService.LoadModsAsync(); // Assuming LoadModsAsync refreshes the list used by GetLoadedMods
                 workshopMods = _modService.GetLoadedMods()
                    .Where(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _) && m.ModType == ModType.WorkshopL)
                    .OrderBy(m => m.Name)
                    .ToList();
             }
             catch (Exception ex)
             {
                 StatusChanged?.Invoke(this, $"Error loading mod list: {ex.Message}");
                 Debug.WriteLine($"[CommandHandler] Error getting mods for update check: {ex}");
                 _dialogService.ShowError("Mod Load Error", $"Failed to load installed mods: {ex.Message}");
                 return;
             }

             if (!workshopMods.Any())
             {
                 StatusChanged?.Invoke(this, "No installed Steam Workshop mods found to check.");
                 _dialogService.ShowInformation("Check Updates", "No installed Steam Workshop mods were found in your mods folder.");
                 return;
             }

             // --- Dialog Flow ---
             // Use the correct namespace for UpdateCheckDialogViewModel and UpdateCheckDialogResult
             var dialogViewModel = new UpdateCheckDialogViewModel(workshopMods);
             // Assuming ShowUpdateCheckDialog returns a specific result enum or similar
             var dialogResult = _dialogService.ShowUpdateCheckDialog(dialogViewModel);

             // Use the specific result type from your Dialog Service/Update Check Dialog
             if (dialogResult != RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck.UpdateCheckDialogResult.CheckUpdates)
             {
                 StatusChanged?.Invoke(this, "Update check cancelled via dialog.");
                 return;
             }
             if (token.IsCancellationRequested) { StatusChanged?.Invoke(this, "Update check cancelled."); return; } // Check token after dialog too

             var selectedMods = dialogViewModel.GetSelectedMods().ToList();
             if (!selectedMods.Any())
             {
                 StatusChanged?.Invoke(this, "No mods were selected for update check.");
                 return;
             }

             // --- Start Actual Operation ---
             OperationStarted?.Invoke(this, EventArgs.Empty);

             ProgressDialogViewModel? progressDialog = null;
             UpdateCheckResult updateResult = new(); // Initialize result

             try
             {
                 StatusChanged?.Invoke(this, $"Checking {selectedMods.Count} mod(s) for updates...");
                 // Pass the linked CTS to the progress dialog
                 progressDialog = _dialogService.ShowProgressDialog("Checking for Updates", "Preparing...", true, true, cts);

                 // Link dialog cancellation (already handled by passing CTS)
                 progressDialog.Cancelled += (s, e) => { StatusChanged?.Invoke(this, "Update check cancelled via dialog."); };


                 // Define progress reporting action
                 var progress = new Progress<(int current, int total, string modName)>(update =>
                 {
                     // Check token within progress update to be responsive
                     if (token.IsCancellationRequested) return;

                     if (progressDialog != null)
                     {
                         progressDialog.Message = $"Checking {update.modName}... ({update.current}/{update.total})";
                         progressDialog.Progress = (int)((double)update.current / update.total * 100);
                         progressDialog.IsIndeterminate = false;
                     }
                     StatusChanged?.Invoke(this, $"Checking {update.modName} ({update.current} of {update.total})...");
                 });

                 // Perform the update check
                 updateResult = await _updateCheckerService.CheckForUpdatesAsync(selectedMods, progress, token);

                 // --- Report Results (only if not cancelled) ---
                 if (token.IsCancellationRequested)
                 {
                    if (progressDialog != null) progressDialog.Message = "Update check cancelled.";
                     StatusChanged?.Invoke(this, "Update check cancelled.");
                     // Don't show dialog, user initiated cancel
                 }
                 else
                 {
                     progressDialog?.CompleteOperation("Update check finished."); // Close dialog nicely
                     string summary = $"Update check complete. Checked: {updateResult.ModsChecked}. Updates found: {updateResult.UpdatesFound}.";
                     if (updateResult.ErrorsEncountered > 0)
                     {
                         summary += $" Errors: {updateResult.ErrorsEncountered}.";
                         // Show limited errors in a separate warning dialog
                         var errorSample = updateResult.ErrorMessages.Take(5).ToList(); // Show slightly more?
                         var errorMessage = string.Join("\n • ", errorSample);
                         if (updateResult.ErrorMessages.Count > 5) { errorMessage += $"\n (and {updateResult.ErrorMessages.Count - 5} more errors...)"; }
                         _dialogService.ShowWarning("Update Check Errors", $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\nCheck status messages or logs for details.\n\nSample Errors:\n • {errorMessage}");
                     }

                     StatusChanged?.Invoke(this, summary); // Update status bar with summary

                     // Show appropriate final dialog
                     if (updateResult.UpdatesFound > 0)
                     {
                         _dialogService.ShowInformation("Updates Found", $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
                         // The AddToQueue calls within the update checker will trigger enrichment via ViewModel's CollectionChanged handler.
                     }
                     else if (updateResult.ErrorsEncountered == 0) // Only show "no updates" if no errors occurred
                     {
                         _dialogService.ShowInformation("No Updates Found", "All selected mods are up to date.");
                     }
                     // If only errors occurred, the warning dialog about errors serves as notification

                     // Re-enrich the entire list *after* the update check, in case mods were added to the queue
                     Debug.WriteLine("[CommandHandler] Update check finished, re-enriching queue items.");
                     _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
                 }
             }
             catch (OperationCanceledException)
             {
                 // Catch cancellation specifically if needed (e.g., user cancelled via dialog/parent)
                if (progressDialog != null) progressDialog.Message = "Update check was cancelled.";
                 StatusChanged?.Invoke(this, "Update check was cancelled.");
                 // No dialog needed here usually
             }
             catch (Exception ex)
             {
                if (progressDialog != null) progressDialog.Message = $"Error: {ex.Message}";
                 StatusChanged?.Invoke(this, $"An error occurred during the update check process: {ex.Message}");
                 Debug.WriteLine($"[CommandHandler] Error executing update check: {ex}");
                 _dialogService.ShowError("Update Check Failed", $"An unexpected error occurred: {ex.Message}");
                 // Re-enrich even on error
                  Debug.WriteLine("[CommandHandler] Update check error, re-enriching queue items.");
                 _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
             }
             finally
             {
                 progressDialog?.ForceClose(); // Ensure closure
                 OperationCompleted?.Invoke(this, EventArgs.Empty); // Notify parent
             }
        }

        // --- Cleanup ---
        public void Cleanup()
        {
             // Unsubscribe from events to prevent memory leaks
             if (_queueServiceStatusHandler != null)
                 _queueService.StatusChanged -= _queueServiceStatusHandler;

             _queueServiceStatusHandler = null; // Clear handler

             Debug.WriteLine("DownloadQueueCommandHandler cleaned up.");
        }
    }
}