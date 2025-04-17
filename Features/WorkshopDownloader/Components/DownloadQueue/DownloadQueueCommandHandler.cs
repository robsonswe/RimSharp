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
        private EventHandler<string>? _queueServiceStatusHandler;

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
            ModInfoEnricher modInfoEnricher)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _updateCheckerService = updateCheckerService ?? throw new ArgumentNullException(nameof(updateCheckerService));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _getCancellationToken = getCancellationToken ?? throw new ArgumentNullException(nameof(getCancellationToken));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _modInfoEnricher = modInfoEnricher ?? throw new ArgumentNullException(nameof(modInfoEnricher));

            _queueServiceStatusHandler = (s, msg) => StatusChanged?.Invoke(this, msg);
            _queueService.StatusChanged += _queueServiceStatusHandler;

            _ = UpdateSteamCmdReadyStatusAsync();
        }

        public bool CanExecuteCheckUpdates()
        {
            try
            {
                return (_modService?.GetLoadedMods().Any(m =>
                    !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _)) ?? false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommandHandler] Error checking CanExecuteCheckUpdates: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error checking mod list: {ex.Message}");
                return false;
            }
        }

        public async Task UpdateSteamCmdReadyStatusAsync()
        {
            await _steamCmdService.CheckSetupAsync();
            bool previousState = IsSteamCmdReady;
            IsSteamCmdReady = _steamCmdService.IsSetupComplete;
            if (IsSteamCmdReady != previousState)
            {
                SteamCmdReadyStatusChanged?.Invoke(this, EventArgs.Empty);
            }
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
                    _dialogService.ShowError("Setup Failed", "SteamCMD setup failed. Please check the logs or status messages for details.");
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
                await UpdateSteamCmdReadyStatusAsync();
                progressDialog?.ForceClose();
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task ExecuteDownloadAsync(CancellationToken token)
        {
            if (!_queueService.Items.Any()) return;
            token = _getCancellationToken();

            OperationStarted?.Invoke(this, EventArgs.Empty);
            var itemsToDownload = _queueService.Items.ToList();
            SteamCmdDownloadResult? downloadResult = null;
            bool refreshIsNeeded = false;
            ProgressDialogViewModel? progressDialog = null;

            try
            {
                StatusChanged?.Invoke(this, $"Starting download of {itemsToDownload.Count} mod(s). Please observe the SteamCMD window...");

                // Show non-cancelable, indeterminate progress dialog
                progressDialog = _dialogService.ShowProgressDialog(
                    "Downloading Mods",
                    $"Starting download of {itemsToDownload.Count} mod(s). Please observe the SteamCMD window...",
                    canCancel: false,
                    isIndeterminate: true,
                    closeable: false
                );

                downloadResult = await _steamCmdService.DownloadModsAsync(itemsToDownload, false, token);

                if (token.IsCancellationRequested) throw new OperationCanceledException();

                StatusChanged?.Invoke(this, "SteamCMD process finished. Processing results...");
                progressDialog.Message = "Processing download results...";
                Debug.WriteLine($"[CommandHandler] SteamCMD finished. Overall Success: {downloadResult.OverallSuccess}, Exit Code: {downloadResult.ExitCode}");

                int successCount = 0;
                int failCount = downloadResult.FailedItems.Count;

                if (downloadResult.SucceededItems.Any())
                {
                    progressDialog.Message = $"Processing {downloadResult.SucceededItems.Count} downloaded mod(s)...";
                    StatusChanged?.Invoke(this, $"Processing {downloadResult.SucceededItems.Count} downloaded mod(s)...");

                    foreach (var successItem in downloadResult.SucceededItems)
                    {
                        if (token.IsCancellationRequested) throw new OperationCanceledException();

                        try
                        {
                            await _modService.CreateTimestampFilesAsync(
                                successItem.SteamId,
                                successItem.PublishDate,
                                successItem.StandardDate);

                            successCount++;
                            refreshIsNeeded = true;
                            _queueService.RemoveFromQueue(successItem);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.WriteLine($"[CommandHandler] Error post-processing success item {successItem.Name} ({successItem.SteamId}): {ex.Message}");
                            failCount++;
                            StatusChanged?.Invoke(this, $"Error processing {successItem.Name}. It remains in the queue.");
                        }
                    }
                }

                string summary;
                if (token.IsCancellationRequested)
                {
                    summary = $"Download cancelled by user. {successCount} items processed before cancellation.";
                    StatusChanged?.Invoke(this, summary);
                    progressDialog.CompleteOperation(summary);
                    _dialogService.ShowInformation("Download Cancelled", summary);
                }
                else if (successCount > 0 && failCount == 0)
                {
                    summary = $"Successfully downloaded and processed {successCount} mod(s).";
                    StatusChanged?.Invoke(this, summary);
                    progressDialog.CompleteOperation(summary);
                    _dialogService.ShowInformation("Download Complete", summary);
                }
                else if (successCount > 0 && failCount > 0)
                {
                    summary = $"Download finished. {successCount} succeeded, {failCount} failed (failed items remain in queue or had post-processing errors).";
                    StatusChanged?.Invoke(this, summary);
                    progressDialog.CompleteOperation(summary);
                    _dialogService.ShowWarning("Download Partially Complete", summary);
                }
                else if (successCount == 0 && failCount > 0)
                {
                    summary = $"Download failed. {failCount} item(s) could not be downloaded or processed. Check SteamCMD log and errors.";
                    StatusChanged?.Invoke(this, summary);
                    progressDialog.CompleteOperation(summary);
                    _dialogService.ShowError("Download Failed", summary);
                }
                else if (downloadResult.ExitCode != 0)
                {
                    summary = $"SteamCMD process failed (Exit Code {downloadResult.ExitCode}). No items successfully processed. Check log.";
                    StatusChanged?.Invoke(this, summary);
                    progressDialog.CompleteOperation(summary);
                    _dialogService.ShowError("Download Failed", summary);
                }
                else
                {
                    summary = "Download process finished, but outcome is unclear. Check SteamCMD log and queue.";
                    StatusChanged?.Invoke(this, summary);
                    progressDialog.CompleteOperation(summary);
                    _dialogService.ShowWarning("Download Finished", summary);
                }

                Debug.WriteLine("[CommandHandler] Download attempt finished, re-enriching remaining queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);

                if (refreshIsNeeded && !token.IsCancellationRequested)
                {
                    Debug.WriteLine("[CommandHandler] Download completed with successes, requesting parent refresh.");
                    DownloadCompletedAndRefreshNeeded?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Download operation cancelled by user.");
                progressDialog?.CompleteOperation("Download operation cancelled.");
                _dialogService.ShowInformation("Cancelled", "Download operation cancelled. Partially downloaded items may exist but were not fully processed.");
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
                progressDialog?.ForceClose();
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task ExecuteAddModAsync(CancellationToken token)
        {
            if (!_browserViewModel.IsValidModUrl || !_browserViewModel.IsModInfoAvailable) return;
            token = _getCancellationToken();

            StatusChanged?.Invoke(this, "Extracting mod info from browser...");
            try
            {
                ModInfoDto? modInfo = await _browserViewModel.GetCurrentModInfoAsync(token);

                if (token.IsCancellationRequested)
                {
                    StatusChanged?.Invoke(this, "Add mod cancelled.");
                    return;
                }

                if (modInfo != null)
                {
                    if (_queueService.AddToQueue(modInfo))
                    {
                        // Success message handled by queue service StatusChanged
                    }
                }
                else
                {
                    StatusChanged?.Invoke(this, "Could not extract mod info from the current page.");
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
                Debug.WriteLine($"[ExecuteAddModAsync] Error: {ex}");
                _dialogService.ShowError("Add Mod Error", $"An error occurred while trying to add the mod: {ex.Message}");
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
                _dialogService.ShowInformation("Item Removed", $"Removed '{item.Name}' from the queue.");
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
                    StatusChanged?.Invoke(this, "Browser cannot navigate now (possibly busy).");
                    Debug.WriteLine($"[CommandHandler] BrowserViewModel.NavigateNAVigateToUrlCommand cannot execute for {url}");
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
            var removedNames = new List<string>();

            foreach (var item in items)
            {
                if (item.Name != null && _queueService.RemoveFromQueue(item))
                {
                    removedCount++;
                    removedNames.Add(item.Name);
                }
            }

            if (removedCount > 0)
            {
                string message = removedCount == 1
                    ? $"Removed '{removedNames[0]}' from the queue."
                    : $"Removed {removedCount} items from the queue.";
                _dialogService.ShowInformation("Items Removed", message);
                StatusChanged?.Invoke(this, $"Removed {removedCount} item(s) from queue.");
            }
        }

        public async Task ExecuteCheckUpdatesAsync(CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_getCancellationToken());
            token = cts.Token;

            List<ModItem> workshopMods;
            try
            {
                StatusChanged?.Invoke(this, "Gathering installed workshop mods...");
                await _modService.LoadModsAsync();
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
                progressDialog = _dialogService.ShowProgressDialog("Checking for Updates", "Preparing...", true, true, cts);
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

                        // --- OLD CODE TO REPLACE ---
                        // var errorSample = updateResult.ErrorMessages.Take(5).ToList();
                        // var errorMessage = string.Join("\n • ", errorSample);
                        // if (updateResult.ErrorMessages.Count > 5) { errorMessage += $"\n (and {updateResult.ErrorMessages.Count - 5} more errors...)"; }
                        // _dialogService.ShowWarning("Update Check Errors", $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\nCheck status messages or logs for details.\n\nSample Errors:\n • {errorMessage}");

                        // --- NEW CODE ---
                        // Combine all error messages into a single string, prefixed with bullet points.
                        var allErrorsMessage = string.Join("\n • ", updateResult.ErrorMessages);

                        // Show the warning dialog with the full list of errors.
                        // Note: If the list is very long, the standard dialog might become unwieldy.
                        // A custom dialog with a scrollable view might be a better UX for many errors.
                        _dialogService.ShowWarning("Update Check Errors",
                            $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\n" +
                            $"Check status messages or logs for more context.\n\n" +
                            $"Errors Reported:\n • {allErrorsMessage}");
                        // --- END NEW CODE ---
                    }

                    StatusChanged?.Invoke(this, summary);

                    if (updateResult.UpdatesFound > 0)
                    {
                        _dialogService.ShowInformation("Updates Found", $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
                    }
                    else if (updateResult.ErrorsEncountered == 0)
                    {
                        _dialogService.ShowInformation("No Updates Found", "All selected mods are up to date.");
                    }

                    Debug.WriteLine("[CommandHandler] Update check finished, re-enriching queue items.");
                    _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
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
            if (_queueServiceStatusHandler != null)
                _queueService.StatusChanged -= _queueServiceStatusHandler;

            _queueServiceStatusHandler = null;
            Debug.WriteLine("DownloadQueueCommandHandler cleaned up.");
        }
    }
}