#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Infrastructure.Workshop;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Collections;
using RimSharp.Infrastructure.Workshop.Download.Models;
using RimSharp.Features.WorkshopDownloader.Dialogs.Collection;
using System.Globalization; // Required for ExecuteRemoveItems parameter

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
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
        private readonly ModInfoEnricher _modInfoEnricher;
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor;
        private readonly ILoggerService _logger;
        private readonly ISteamApiClient _steamApiClient;

        private EventHandler<string>? _queueServiceStatusHandler;
        private EventHandler<bool>? _steamCmdSetupStateChangedHandler; // Keep handler reference for unsubscribing

        public bool IsSteamCmdReady { get; private set; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationStarted;
        public event EventHandler? OperationCompleted;
        public event EventHandler? DownloadCompletedAndRefreshNeeded;
        public event EventHandler? SteamCmdReadyStatusChanged;

        public DownloadQueueCommandHandler(
            IDownloadQueueService queueService,
            IModService modService,
            IDialogService dialogService,
            IWorkshopUpdateCheckerService updateCheckerService,
            ISteamCmdService steamCmdService,
            BrowserViewModel browserViewModel,
            Func<CancellationToken> getCancellationToken,
            IModListManager modListManager,
            ModInfoEnricher modInfoEnricher,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor,
            ILoggerService logger,
            ISteamApiClient steamApiClient)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService)); // Keep for mod list check
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _updateCheckerService = updateCheckerService ?? throw new ArgumentNullException(nameof(updateCheckerService));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _getCancellationToken = getCancellationToken ?? throw new ArgumentNullException(nameof(getCancellationToken));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _modInfoEnricher = modInfoEnricher ?? throw new ArgumentNullException(nameof(modInfoEnricher));
            _steamWorkshopQueueProcessor = steamWorkshopQueueProcessor ?? throw new ArgumentNullException(nameof(steamWorkshopQueueProcessor));
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));


            _queueServiceStatusHandler = (s, msg) => StatusChanged?.Invoke(this, msg);
            _queueService.StatusChanged += _queueServiceStatusHandler;

            // Subscribe to SteamCMD setup changes
            _steamCmdSetupStateChangedHandler = SteamCmdService_SetupStateChanged; // Store handler reference
            _steamCmdService.SetupStateChanged += _steamCmdSetupStateChangedHandler;
            _ = UpdateSteamCmdReadyStatusAsync(); // Initial check
        }

        // Event handler for SetupStateChanged
        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() =>
            { // Ensure UI thread if needed for bindings
                bool previousState = IsSteamCmdReady;
                IsSteamCmdReady = isSetup;
                if (IsSteamCmdReady != previousState)
                {
                    SteamCmdReadyStatusChanged?.Invoke(this, EventArgs.Empty);
                    StatusChanged?.Invoke(this, $"SteamCMD Status: {(isSetup ? "Ready" : "Not Ready")}");
                    Debug.WriteLine($"[CommandHandler] SteamCmdReady updated to: {IsSteamCmdReady}");
                }
            });
        }

        // Helper to run actions on the UI thread if necessary (implement using Dispatcher or SynchronizationContext)
        private void RunOnUIThread(Action action)
        {
            // Example using Application.Current.Dispatcher for WPF:
            System.Windows.Application.Current?.Dispatcher?.Invoke(action);
            // Or use appropriate mechanism for your UI framework (e.g., SynchronizationContext.Post/Send)
        }


        public bool CanExecuteCheckUpdates()
        {
            // Keep _modService dependency for this check
            try
            {
                // Ensure mods are loaded before checking
                // Note: Consider if LoadModsAsync should be awaited here or if it's expected to be loaded elsewhere.
                // Assuming GetLoadedMods returns currently known mods.
                return (_modService?.GetLoadedMods().Any(m =>
                    !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _) && m.ModType == ModType.WorkshopL) ?? false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandler] Error checking CanExecuteCheckUpdates: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error checking mod list: {ex.Message}");
                return false;
            }
        }

        // This can be simplified as the event handler now drives the state
        public async Task UpdateSteamCmdReadyStatusAsync()
        {
            bool isReady = await _steamCmdService.CheckSetupAsync();
            // The event handler SteamCmdService_SetupStateChanged will update IsSteamCmdReady
            // This initial check ensures IsSteamCmdReady is correct on startup before the first event might fire.
            RunOnUIThread(() =>
            {
                bool previousState = IsSteamCmdReady;
                IsSteamCmdReady = isReady;
                if (IsSteamCmdReady != previousState)
                {
                    SteamCmdReadyStatusChanged?.Invoke(this, EventArgs.Empty); // Fire event if changed on initial check
                    Debug.WriteLine($"[CommandHandler] Initial SteamCmdReady check: {IsSteamCmdReady}");
                }
            });
        }

        public async Task ExecuteSetupSteamCmdAsync(CancellationToken token)
        {
            OperationStarted?.Invoke(this, EventArgs.Empty);
            ProgressDialogViewModel? progressDialog = null;
            try
            {
                progressDialog = _dialogService.ShowProgressDialog("SteamCMD Setup", "Starting setup...", true, true, CancellationTokenSource.CreateLinkedTokenSource(token));
                progressDialog.Cancelled += (s, e) => StatusChanged?.Invoke(this, "Setup cancelled via dialog.");

                var progressReporter = new Progress<string>(message =>
                {
                    if (progressDialog != null) progressDialog.Message = message;
                    StatusChanged?.Invoke(this, message);
                });

                StatusChanged?.Invoke(this, "Running SteamCMD Setup...");
                bool success = await _steamCmdService.SetupAsync(progressReporter, token);

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
                    if (progressDialog != null) progressDialog.Message = "Setup failed. See log/messages.";
                    StatusChanged?.Invoke(this, "SteamCMD setup failed.");
                    // Dialog shown by the SetupAsync method on failure usually
                }
            }
            catch (OperationCanceledException)
            {
                if (progressDialog != null) progressDialog.Message = "Setup cancelled.";
                StatusChanged?.Invoke(this, "SteamCMD setup cancelled.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during SteamCMD setup: {ex.Message}");
                if (progressDialog != null) progressDialog.Message = $"Error: {ex.Message}";
                _dialogService.ShowError("Setup Error", $"An unexpected error occurred during setup: {ex.Message}");
                Debug.WriteLine($"[ExecuteSetupSteamCmdAsync] Error: {ex}");
            }
            finally
            {
                // Status is updated via SetupStateChanged event now
                progressDialog?.ForceClose();
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task ExecuteDownloadAsync(CancellationToken token)
        {
            if (!_queueService.Items.Any())
            {
                StatusChanged?.Invoke(this, "Download queue is empty.");
                return;
            }
            if (!IsSteamCmdReady)
            {
                StatusChanged?.Invoke(this, "SteamCMD is not ready. Please run setup first.");
                _dialogService.ShowWarning("SteamCMD Not Ready", "SteamCMD setup needs to be completed before downloading mods.");
                return;
            }

            token = _getCancellationToken(); // Get the active cancellation token

            OperationStarted?.Invoke(this, EventArgs.Empty);
            var itemsToDownload = _queueService.Items.ToList(); // Copy the list
            SteamCmdDownloadResult? downloadResult = null;
            bool refreshIsNeeded = false;
            ProgressDialogViewModel? progressDialog = null;

            try
            {
                StatusChanged?.Invoke(this, $"Starting download of {itemsToDownload.Count} mod(s). Please observe the SteamCMD window...");

                // Show non-cancelable, indeterminate progress dialog during SteamCMD execution
                progressDialog = _dialogService.ShowProgressDialog(
                    "Downloading Mods",
                    $"Running SteamCMD for {itemsToDownload.Count} mod(s). See console window for details...",
                    canCancel: false, // SteamCMD cancellation happens via main cancel button/token
                    isIndeterminate: true,
                    closeable: false // Keep open until processing finishes
                );

                // Call the modified download service method. It now handles timestamping and moving internally.
                downloadResult = await _steamCmdService.DownloadModsAsync(itemsToDownload, false, token);

                // Check for cancellation *after* SteamCMD finishes but *before* processing results
                if (token.IsCancellationRequested) throw new OperationCanceledException();

                StatusChanged?.Invoke(this, "SteamCMD process finished. Processing results...");
                progressDialog.Message = "Processing download results..."; // Update dialog message
                Debug.WriteLine($"[CommandHandler] SteamCMD finished. Overall Success: {downloadResult.OverallSuccess}, Exit Code: {downloadResult.ExitCode}, Succeeded Items: {downloadResult.SucceededItems.Count}, Failed Items: {downloadResult.FailedItems.Count}");

                // --- Simplified Result Processing ---
                // Items in downloadResult.SucceededItems are ALREADY timestamped and moved.
                // Items in downloadResult.FailedItems either failed download OR failed post-processing.

                if (downloadResult.SucceededItems.Any())
                {
                    refreshIsNeeded = true; // Need to refresh UI/mod list
                    StatusChanged?.Invoke(this, $"Successfully processed {downloadResult.SucceededItems.Count} mod(s). Removing from queue...");
                    progressDialog.Message = $"Successfully processed {downloadResult.SucceededItems.Count} mod(s). Removing from queue...";

                    // Remove successfully processed items from the queue
                    foreach (var successItem in downloadResult.SucceededItems)
                    {
                        if (token.IsCancellationRequested) throw new OperationCanceledException();
                        _queueService.RemoveFromQueue(successItem); // Remove silently or log debug
                        Debug.WriteLine($"[CommandHandler] Removed successfully processed item {successItem.SteamId} from queue.");
                    }
                }

                if (downloadResult.FailedItems.Any())
                {
                    StatusChanged?.Invoke(this, $"{downloadResult.FailedItems.Count} item(s) failed download or post-processing. They remain in the queue. Check logs/messages for details.");
                    // Items remain in the queue automatically as they weren't removed.
                    // Optionally add more details from downloadResult.LogMessages here if needed.
                }


                // --- Determine Final Summary Message ---
                string summary;
                if (token.IsCancellationRequested)
                {
                    summary = $"Download cancelled by user. {downloadResult.SucceededItems.Count} items successfully processed before cancellation.";
                    // Items processed before cancel are already removed from queue. Others remain.
                }
                else if (downloadResult.OverallSuccess)
                {
                    if (downloadResult.SucceededItems.Any())
                        summary = $"Successfully downloaded and processed {downloadResult.SucceededItems.Count} mod(s).";
                    else
                        summary = "Download process completed, and no items failed, but no items were successfully processed (queue might have been empty initially or items were invalid).";
                }
                else // OverallSuccess is false
                {
                    if (downloadResult.FailedItems.Any() && downloadResult.SucceededItems.Any())
                        summary = $"Download finished with issues. {downloadResult.SucceededItems.Count} succeeded, {downloadResult.FailedItems.Count} failed (failed items remain in queue). Check messages/logs for details.";
                    else if (downloadResult.FailedItems.Any())
                        summary = $"Download failed. {downloadResult.FailedItems.Count} item(s) could not be downloaded or processed. Check SteamCMD log and status messages for errors.";
                    else if (downloadResult.ExitCode != 0)
                        summary = $"SteamCMD process failed (Exit Code {downloadResult.ExitCode}). No items successfully processed. Check log.";
                    else
                        summary = "Download process finished with errors, but details unclear. Check SteamCMD log and queue.";
                }

                // --- Show Final Dialog ---
                progressDialog.CompleteOperation(summary); // Update dialog one last time
                StatusChanged?.Invoke(this, summary);
                Debug.WriteLine($"[CommandHandler] Download Summary: {summary}");

                // Choose dialog type based on outcome
                if (token.IsCancellationRequested)
                    _dialogService.ShowInformation("Download Cancelled", summary);
                else if (downloadResult.OverallSuccess && downloadResult.SucceededItems.Any())
                    _dialogService.ShowInformation("Download Complete", summary);
                else if (!downloadResult.OverallSuccess && downloadResult.SucceededItems.Any()) // Partial success
                    _dialogService.ShowWarning("Download Partially Complete", summary);
                else if (!downloadResult.OverallSuccess && downloadResult.FailedItems.Any()) // All failed or process error
                    _dialogService.ShowError("Download Failed", summary);
                else // Other cases (e.g., no items succeeded but no explicit failures reported / exit code 0)
                    _dialogService.ShowInformation("Download Finished", summary);


                Debug.WriteLine("[CommandHandler] Download attempt finished, re-enriching remaining queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items); // Refresh info for failed/remaining items

                if (refreshIsNeeded && !token.IsCancellationRequested)
                {
                    Debug.WriteLine("[CommandHandler] Download completed with successes, requesting parent refresh.");
                    DownloadCompletedAndRefreshNeeded?.Invoke(this, EventArgs.Empty); // Signal UI refresh
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Download operation cancelled by user.");
                progressDialog?.CompleteOperation("Download operation cancelled.");
                _dialogService.ShowInformation("Cancelled", "Download operation cancelled. Partially processed items may exist.");
                Debug.WriteLine("[CommandHandler] Download cancelled, re-enriching queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during download process: {ex.Message}");
                progressDialog?.CompleteOperation($"Error: {ex.Message}");
                Debug.WriteLine($"[ExecuteDownloadAsync] Exception: {ex}");
                _dialogService.ShowError("Download Error", $"An unexpected error occurred during the download: {ex.Message}");
                Debug.WriteLine("[CommandHandler] Download error occurred, re-enriching queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
            }
            finally
            {
                progressDialog?.ForceClose(); // Ensure dialog closes
                OperationCompleted?.Invoke(this, EventArgs.Empty); // Signal operation end
            }
        }

        public async Task ExecuteAddModAsync(CancellationToken token)
        {
            if (!IsSteamCmdReady)
            {
                StatusChanged?.Invoke(this, "Cannot add mod: SteamCMD is not ready.");
                _dialogService.ShowWarning("SteamCMD Not Ready", "SteamCMD setup needs to be completed before adding mods.");
                return;
            }

            // Use combined token
            token = CancellationTokenSource.CreateLinkedTokenSource(_getCancellationToken(), token).Token;

            OperationStarted?.Invoke(this, EventArgs.Empty);
            try
            {
                if (_browserViewModel.IsCollectionUrl)
                {
                    // --- Handle Collection ---
                    await AddItemsFromCollectionAsync(token);
                }
                else if (_browserViewModel.IsValidModUrl)
                {
                    // --- Handle Single Mod ---
                    await AddSingleModFromBrowserAsync(token);
                }
                else
                {
                    StatusChanged?.Invoke(this, "Cannot add mod: Current page is not a valid mod or collection page, or required info is missing.");
                    _dialogService.ShowWarning("Cannot Add Mod", "The current browser page is not recognized as a valid mod or collection, or essential information couldn't be extracted.");
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Add mod operation cancelled.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error adding mod(s): {ex.Message}");
                Debug.WriteLine($"[ExecuteAddModAsync] Error: {ex}");
                _dialogService.ShowError("Add Mod Error", $"An error occurred while trying to add the mod(s): {ex.Message}");
            }
            finally
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task AddSingleModFromBrowserAsync(CancellationToken token)
        {
            ModInfoDto? modInfo = null;
            string? failedReason = null;
            string? steamId = null;
            string? url = _browserViewModel.ActualCurrentUrl; // Get current URL

            // --- 1. Extract Steam ID from URL ---
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                steamId = queryParams["id"];
            }

            if (string.IsNullOrEmpty(steamId))
            {
                failedReason = "Could not extract Steam ID from the browser URL.";
                _dialogService.ShowError("Add Mod Failed", failedReason);
                StatusChanged?.Invoke(this, failedReason);
                return;
            }

            // --- 2. Try ModExtractorService first ---
            try
            {
                StatusChanged?.Invoke(this, "Attempting to extract mod info from browser page...");
                modInfo = await _browserViewModel.GetCurrentModInfoAsync(token);
                token.ThrowIfCancellationRequested();
                if (modInfo != null)
                {
                    Debug.WriteLine($"[CommandHandler] Successfully extracted mod info via browser: {modInfo.Name}");
                }
                else
                {
                    Debug.WriteLine("[CommandHandler] Browser extraction returned null.");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandler] Error during browser extraction: {ex.Message}");
                // Don't fail yet, proceed to API fallback
                StatusChanged?.Invoke(this, "Browser extraction failed, trying Steam API fallback...");
            }

            // --- 3. If browser extraction failed, try Steam API fallback ---
            if (modInfo == null)
            {
                Debug.WriteLine($"[CommandHandler] Attempting Steam API fallback for ID: {steamId}");
                StatusChanged?.Invoke(this, $"Trying Steam API fallback for ID {steamId}...");
                try
                {
                    SteamApiResponse? apiResponse = await _steamApiClient.GetFileDetailsAsync(steamId, token);
                    token.ThrowIfCancellationRequested();

                    if (apiResponse?.Response?.PublishedFileDetails?.FirstOrDefault() is SteamPublishedFileDetails details && details.Result == 1)
                    {
                        DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
                        modInfo = new ModInfoDto
                        {
                            Name = details.Title ?? $"Unknown Mod {steamId}",
                            SteamId = steamId,
                            Url = url, // Use the original browser URL
                            PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                            StandardDate = apiUpdateTimeOffset.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                            FileSize = details.FileSize
                        };
                        Debug.WriteLine($"[CommandHandler] Successfully obtained mod info via Steam API: {modInfo.Name}");
                        StatusChanged?.Invoke(this, "Successfully obtained mod info via Steam API.");
                    }
                    else
                    {
                        string errorDescription = "Unknown API failure.";
                        if (apiResponse?.Response?.PublishedFileDetails?.FirstOrDefault() is SteamPublishedFileDetails errorDetails)
                        {
                            errorDescription = SteamApiResultHelper.GetDescription(errorDetails.Result);
                        }
                        else if (apiResponse == null)
                        {
                            errorDescription = "API request failed or returned no data.";
                        }
                        Debug.WriteLine($"[CommandHandler] Steam API fallback failed for ID {steamId}. Reason: {errorDescription}");
                        failedReason = $"Steam API fallback failed: {errorDescription}";
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CommandHandler] Error during Steam API fallback: {ex.Message}");
                    failedReason = $"Steam API fallback failed with error: {ex.Message}";
                }
            }

            // --- 4. Add to queue if successful, otherwise show error ---
            if (modInfo != null)
            {
                if (_queueService.AddToQueue(modInfo))
                {
                    // Queue service raises its own status message on success
                    Debug.WriteLine($"[CommandHandler] Added '{modInfo.Name}' to queue.");
                    // StatusChanged?.Invoke(this, $"Added '{modInfo.Name}' to download queue."); // Optional: Redundant if queue service already does it
                }
                // Else: Queue service handles duplicates/errors silently or via its own status message
            }
            else
            {
                // If modInfo is still null, both methods failed
                failedReason ??= "Could not retrieve mod information using browser extraction or Steam API.";
                _dialogService.ShowError("Add Mod Failed", failedReason);
                StatusChanged?.Invoke(this, failedReason);
            }
        }


        private async Task AddItemsFromCollectionAsync(CancellationToken token)
        {
            StatusChanged?.Invoke(this, "Extracting collection items from browser...");
            List<CollectionItemInfo> collectionItems = await _browserViewModel.ExtractCollectionItemsAsync(token);

            token.ThrowIfCancellationRequested();

            if (collectionItems == null || !collectionItems.Any())
            {
                StatusChanged?.Invoke(this, "No items found in the collection or extraction failed.");
                _dialogService.ShowWarning("Extraction Failed", "Could not find any items in the collection on the current page, or an error occurred during extraction.");
                return;
            }

            StatusChanged?.Invoke(this, $"Found {collectionItems.Count} items. Showing selection dialog...");

            // --- Show Selection Dialog ---
            var dialogViewModel = new CollectionDialogViewModel(collectionItems);
            List<string>? selectedIds = _dialogService.ShowCollectionDialog(dialogViewModel); // Runs on UI thread internally

            token.ThrowIfCancellationRequested();

            if (selectedIds == null)
            {
                StatusChanged?.Invoke(this, "Collection add cancelled by user.");
                return; // User cancelled the dialog
            }

            if (!selectedIds.Any())
            {
                StatusChanged?.Invoke(this, "No items selected from the collection to add.");
                return; // User selected none
            }

            // --- Process Selected Items ---
            StatusChanged?.Invoke(this, $"Adding {selectedIds.Count} selected item(s) to queue via Steam API check...");
            ProgressDialogViewModel? progressDialog = null;
            QueueProcessResult? processResult = null;

            // --- Prepare linked cancellation source for the progress dialog ---
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token); // Use using for disposal

            try
            {
                progressDialog = _dialogService.ShowProgressDialog(
                    "Processing Collection Items",
                    "Starting API checks...",
                    canCancel: true, // Allow cancellation of the API processing
                    isIndeterminate: false, // Show progress
                    cts: linkedCts, // Pass the LINKED CTS
                    closeable: false // Keep open until done
                );
                // Note: Cancelled event handler is attached internally by ShowProgressDialog or its view now

                var progressReporter = new Progress<QueueProcessProgress>(p =>
                {
                    // --- FIX: Use CancellationToken property ---
                    if (progressDialog?.CancellationToken.IsCancellationRequested ?? true) return;
                    RunOnUIThread(() =>
                    {
                        if (progressDialog != null) // Check again after switching thread
                        {
                            progressDialog.Progress = (int)(((double)p.CurrentItem / p.TotalItems) * 100);
                            progressDialog.Message = $"Checking {p.CurrentItemName ?? p.CurrentSteamId}... ({p.CurrentItem}/{p.TotalItems})\n{p.Message}";
                        }
                        StatusChanged?.Invoke(this, $"Processing {p.CurrentItem}/{p.TotalItems}: {p.CurrentItemName ?? p.CurrentSteamId} - {p.Message}");
                    });
                });

                // Use the injected queue processor
                processResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(
                    selectedIds,
                    progressReporter,
                    linkedCts.Token // --- FIX: Use the token from the LINKED source ---
                );

                // --- Show Summary (unchanged) ---
                string summary;
                if (processResult.WasCancelled)
                {
                    summary = $"Processing cancelled. Successfully added {processResult.SuccessfullyAdded} mods before cancellation.";
                    // Don't call CompleteOperation if already cancelled? Or call with cancel message?
                    progressDialog?.ForceClose(); // Force close if cancelled externally or via button
                    _dialogService.ShowInformation("Processing Cancelled", summary);
                }
                else
                {
                    summary = $"Processing complete. Attempted: {processResult.TotalAttempted}, Added: {processResult.SuccessfullyAdded}, Already Queued: {processResult.AlreadyQueued}, Failed: {processResult.FailedProcessing}.";
                    progressDialog?.CompleteOperation("Processing Complete."); // Complete normally

                    if (processResult.FailedProcessing > 0)
                    {
                        string errors = string.Join("\n- ", processResult.ErrorMessages.Take(5)); // Show first 5 errors
                        _dialogService.ShowWarning("Processing Issues", $"{summary}\n\nSome items failed:\n- {errors}{(processResult.ErrorMessages.Count > 5 ? "\n..." : "")}");
                    }
                    else
                    {
                        _dialogService.ShowInformation("Processing Complete", summary);
                    }
                }
                StatusChanged?.Invoke(this, summary);
            }
            catch (OperationCanceledException) // Catch cancellation from the LINKED token
            {
                StatusChanged?.Invoke(this, "Collection item processing cancelled.");
                progressDialog?.ForceClose(); // Force close on cancellation
                                              // Optionally show an info dialog, but might be redundant if cancelled via UI
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error processing collection items: {ex.Message}");
                progressDialog?.CompleteOperation($"Error: {ex.Message}"); // Show error before force close
                Debug.WriteLine($"[AddItemsFromCollectionAsync] Error: {ex}");
                _dialogService.ShowError("Processing Error", $"An unexpected error occurred while processing collection items: {ex.Message}");
            }
            finally
            {
                progressDialog?.ForceClose(); // Ensure it's closed
            }
        }

        public void ExecuteRemoveItem(DownloadItem? item)
        {
            if (item == null) return;

            var result = _dialogService.ShowConfirmation("Remove from Queue",
                $"Are you sure you want to remove '{item.Name}' from the queue?",
                showCancel: true);

            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            if (_queueService.RemoveFromQueue(item))
            {
                // Message handled by queue service StatusChanged
                // Optionally show an info dialog here too
                // _dialogService.ShowInformation("Item Removed", $"Removed '{item.Name}' from the queue.");
                StatusChanged?.Invoke(this, $"Removed '{item.Name}' from the queue.");
            }
        }

        public void ExecuteNavigateToUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                Debug.WriteLine($"[CommandHandler] Requesting navigation to: {url}");
                if (_browserViewModel.NavigateToUrlCommand.CanExecute(url))
                {
                    _browserViewModel.NavigateToUrlCommand.Execute(url);
                    StatusChanged?.Invoke(this, $"Navigating browser to: {url}");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Browser cannot navigate now (possibly busy or invalid URL).");
                    Debug.WriteLine($"[CommandHandler] BrowserViewModel.NavigateToUrlCommand cannot execute for {url}");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to initiate browser navigation: {ex.Message}");
                _dialogService.ShowError("Navigation Error", $"Could not navigate the internal browser: {ex.Message}");
                Debug.WriteLine($"[CommandHandler] Error executing NavigateToUrl: {ex}");
            }
        }

        public void ExecuteRemoveItems(System.Collections.IList? selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0) return;

            var items = selectedItems.Cast<DownloadItem>().ToList();
            string confirmMessage = items.Count == 1
                ? $"Are you sure you want to remove '{items[0].Name}' from the queue?"
                : $"Are you sure you want to remove {items.Count} items from the queue?";

            var result = _dialogService.ShowConfirmation("Remove from Queue", confirmMessage, showCancel: true);
            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            int removedCount = 0;
            var removedNames = new List<string>(); // Store names for summary

            foreach (var item in items)
            {
                if (item.Name != null && _queueService.RemoveFromQueue(item))
                {
                    removedCount++;
                    removedNames.Add(item.Name); // Add name if successfully removed
                }
            }

            if (removedCount > 0)
            {
                string message = removedCount == 1
                    ? $"Removed '{removedNames.FirstOrDefault() ?? "item"}' from the queue." // Use FirstOrDefault for safety
                    : $"Removed {removedCount} items from the queue.";
                // Message handled by queue service StatusChanged
                // Optionally show an info dialog here too
                // _dialogService.ShowInformation("Items Removed", message);
                StatusChanged?.Invoke(this, $"Removed {removedCount} item(s) from queue.");
            }
        }

        public async Task ExecuteCheckUpdatesAsync(CancellationToken token)
        {
            if (!IsSteamCmdReady)
            {
                StatusChanged?.Invoke(this, "Cannot check updates: SteamCMD is not ready.");
                _dialogService.ShowWarning("SteamCMD Not Ready", "SteamCMD setup needs to be completed before checking for mod updates.");
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_getCancellationToken(), token); // Combine tokens
            token = cts.Token; // Use combined token

            List<ModItem> workshopMods;
            try
            {
                StatusChanged?.Invoke(this, "Gathering installed workshop mods...");
                // Ensure mods are loaded before checking
                await _modService.LoadModsAsync(); // Ensure latest mod list is loaded
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

            var dialogViewModel = new UpdateCheckDialogViewModel(workshopMods);
            var dialogResult = _dialogService.ShowUpdateCheckDialog(dialogViewModel);

            if (dialogResult != RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck.UpdateCheckDialogResult.CheckUpdates)
            {
                StatusChanged?.Invoke(this, "Update check cancelled via dialog.");
                return;
            }
            if (token.IsCancellationRequested) { StatusChanged?.Invoke(this, "Update check cancelled."); return; }

            var selectedMods = dialogViewModel.GetSelectedMods().ToList();
            if (!selectedMods.Any())
            {
                StatusChanged?.Invoke(this, "No mods were selected for update check.");
                return;
            }

            OperationStarted?.Invoke(this, EventArgs.Empty);
            ProgressDialogViewModel? progressDialog = null;
            UpdateCheckResult updateResult = new();

            try
            {
                StatusChanged?.Invoke(this, $"Checking {selectedMods.Count} mod(s) for updates...");
                progressDialog = _dialogService.ShowProgressDialog("Checking for Updates", "Preparing...", true, true, cts); // Use combined CTS
                progressDialog.Cancelled += (s, e) => StatusChanged?.Invoke(this, "Update check cancelled via dialog.");

                var progress = new Progress<(int current, int total, string modName)>(update =>
                {
                    if (token.IsCancellationRequested) return;
                    if (progressDialog != null)
                    {
                        progressDialog.Message = $"Checking {update.modName}... ({update.current}/{update.total})";
                        progressDialog.Progress = (int)((double)update.current / update.total * 100);
                        progressDialog.IsIndeterminate = false;
                    }
                    StatusChanged?.Invoke(this, $"Checking {update.modName} ({update.current} of {update.total})...");
                });

                updateResult = await _updateCheckerService.CheckForUpdatesAsync(selectedMods, progress, token);

                if (token.IsCancellationRequested)
                {
                    if (progressDialog != null) progressDialog.Message = "Update check cancelled.";
                    StatusChanged?.Invoke(this, "Update check cancelled.");
                }
                else
                {
                    progressDialog?.CompleteOperation("Update check finished.");
                    string summary = $"Update check complete. Checked: {updateResult.ModsChecked}. Updates found: {updateResult.UpdatesFound}.";
                    if (updateResult.ErrorsEncountered > 0)
                    {
                        summary += $" Errors: {updateResult.ErrorsEncountered}.";

                        var allErrorsMessage = string.Join("\n • ", updateResult.ErrorMessages);
                        _dialogService.ShowWarning("Update Check Errors",
                            $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\n" +
                            $"Check status messages or logs for more context.\n\n" +
                            $"Errors Reported:\n • {allErrorsMessage}");
                    }

                    StatusChanged?.Invoke(this, summary);

                    if (updateResult.UpdatesFound > 0)
                    {
                        // Items are added to queue by the UpdateCheckerService, show confirmation
                        _dialogService.ShowInformation("Updates Found", $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
                    }
                    else if (updateResult.ErrorsEncountered == 0)
                    {
                        _dialogService.ShowInformation("No Updates Found", "All selected mods appear to be up to date.");
                    }

                    Debug.WriteLine("[CommandHandler] Update check finished, re-enriching queue items.");
                    _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items); // Refresh queue info
                }
            }
            catch (OperationCanceledException)
            {
                if (progressDialog != null) progressDialog.Message = "Update check was cancelled.";
                StatusChanged?.Invoke(this, "Update check was cancelled.");
            }
            catch (Exception ex)
            {
                if (progressDialog != null) progressDialog.Message = $"Error: {ex.Message}";
                StatusChanged?.Invoke(this, $"An error occurred during the update check process: {ex.Message}");
                Debug.WriteLine($"[CommandHandler] Error executing update check: {ex}");
                _dialogService.ShowError("Update Check Failed", $"An unexpected error occurred: {ex.Message}");
                Debug.WriteLine("[CommandHandler] Update check error, re-enriching queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
            }
            finally
            {
                progressDialog?.ForceClose();
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }


        public void Cleanup()
        {
            // Unsubscribe from events
            if (_queueServiceStatusHandler != null && _queueService != null)
                _queueService.StatusChanged -= _queueServiceStatusHandler;
            if (_steamCmdSetupStateChangedHandler != null && _steamCmdService != null)
                _steamCmdService.SetupStateChanged -= _steamCmdSetupStateChangedHandler;


            // Clear handler references
            _queueServiceStatusHandler = null;
            _steamCmdSetupStateChangedHandler = null;
            Debug.WriteLine("DownloadQueueCommandHandler cleaned up.");
        }
    }
}