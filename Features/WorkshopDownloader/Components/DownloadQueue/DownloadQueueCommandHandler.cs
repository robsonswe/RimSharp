#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
using RimSharp.Core.Extensions;
using System.Collections;
using RimSharp.Infrastructure.Workshop.Download.Models;
using RimSharp.Features.WorkshopDownloader.Dialogs.Collection;
using System.Globalization;

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
        private EventHandler<bool>? _steamCmdSetupStateChangedHandler;

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
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
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

            _steamCmdSetupStateChangedHandler = SteamCmdService_SetupStateChanged;
            _steamCmdService.SetupStateChanged += _steamCmdSetupStateChangedHandler;
            _ = UpdateSteamCmdReadyStatusAsync();
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() =>
            {
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

        private void RunOnUIThread(Action action)
        {
            ThreadHelper.EnsureUiThread(action);
        }

        public bool CanExecuteCheckUpdates()
        {
            try
            {
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

        public async Task UpdateSteamCmdReadyStatusAsync()
        {
            bool isReady = await _steamCmdService.CheckSetupAsync();

            RunOnUIThread(() =>
            {
                bool previousState = IsSteamCmdReady;
                IsSteamCmdReady = isReady;
                if (IsSteamCmdReady != previousState)
                {
                    SteamCmdReadyStatusChanged?.Invoke(this, EventArgs.Empty);
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
                progressDialog = _dialogService.ShowProgressDialog("SteamCMD Setup", "Starting setup...", true, true, CancellationTokenSource.CreateLinkedTokenSource(token), false);
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
                    await _dialogService.ShowInformation("Setup Complete", "SteamCMD has been set up successfully.");
                }
                else
                {
                    if (progressDialog != null) progressDialog.Message = "Setup failed. See log/messages.";
                    StatusChanged?.Invoke(this, "SteamCMD setup failed.");
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
                await _dialogService.ShowError("Setup Error", $"An unexpected error occurred during setup: {ex.Message}");
                Debug.WriteLine($"[ExecuteSetupSteamCmdAsync] Error: {ex}");
            }
            finally
            {
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
                await _dialogService.ShowWarning("SteamCMD Not Ready", "SteamCMD setup needs to be completed before downloading mods.");
                return;
            }

            token = _getCancellationToken();

            OperationStarted?.Invoke(this, EventArgs.Empty);
            var itemsToDownload = _queueService.Items.ToList();
            SteamCmdDownloadResult? downloadResult = null;
            bool refreshIsNeeded = false;
            ProgressDialogViewModel? progressDialog = null;

            try
            {
                StatusChanged?.Invoke(this, $"Downloading {itemsToDownload.Count} mod(s) trough SteamCMD. Do not close the app...");

                progressDialog = _dialogService.ShowProgressDialog(
                    "Downloading Mods",
                    $"Downloading {itemsToDownload.Count} mod(s) trough SteamCMD. Do not close the app...",
                    canCancel: false,
                    isIndeterminate: true,
                    closeable: false
                );
                var progress = new Progress<(int current, int total, string message)>(update =>
                {
                    if (token.IsCancellationRequested) return;
                    RunOnUIThread(() =>
                    {
                        if (progressDialog != null)
                        {
                            progressDialog.IsIndeterminate = false;
                            progressDialog.Progress = (int)((double)update.current / update.total * 100);
                            progressDialog.Message = $"{update.message} ({update.current}/{update.total})";
                        }
                        StatusChanged?.Invoke(this, $"{update.message} ({update.current} of {update.total})...");
                    });
                });

                downloadResult = await _steamCmdService.DownloadModsAsync(itemsToDownload, false, progress, token);

                if (token.IsCancellationRequested) throw new OperationCanceledException();

                StatusChanged?.Invoke(this, "SteamCMD process finished. Processing results...");
                progressDialog.Message = "Processing download results...";
                Debug.WriteLine($"[CommandHandler] SteamCMD finished. Overall Success: {downloadResult.OverallSuccess}, Exit Code: {downloadResult.ExitCode}, Succeeded Items: {downloadResult.SucceededItems.Count}, Failed Items: {downloadResult.FailedItems.Count}");

                if (downloadResult.SucceededItems.Any())
                {
                    refreshIsNeeded = true;
                    StatusChanged?.Invoke(this, $"Successfully processed {downloadResult.SucceededItems.Count} mod(s). Removing from queue...");
                    progressDialog.Message = $"Successfully processed {downloadResult.SucceededItems.Count} mod(s). Removing from queue...";
                    foreach (var successItem in downloadResult.SucceededItems)
                    {
                        if (token.IsCancellationRequested) throw new OperationCanceledException();
                        _queueService.RemoveFromQueue(successItem);
                        Debug.WriteLine($"[CommandHandler] Removed successfully processed item {successItem.SteamId} from queue.");
                    }
                }

                if (downloadResult.FailedItems.Any())
                {
                    StatusChanged?.Invoke(this, $"{downloadResult.FailedItems.Count} item(s) failed download or post-processing. They remain in the queue. Check logs/messages for details.");
                }

                string summaryTitle = "Download Process Finished";
                var summaryMessageBuilder = new StringBuilder();

                if (token.IsCancellationRequested)
                {
                    summaryTitle = "Download Cancelled";
                    summaryMessageBuilder.AppendLine($"Download cancelled by user. {downloadResult.SucceededItems.Count} items were successfully processed before cancellation.");
                }
                else if (downloadResult.OverallSuccess)
                {
                    summaryTitle = "Download Complete";
                    summaryMessageBuilder.AppendLine($"Successfully downloaded and processed {downloadResult.SucceededItems.Count} mod(s).");
                }
                else 
                {
                    if (downloadResult.FailedItems.Any())
                    {
                        summaryTitle = downloadResult.SucceededItems.Any() ? "Download Partially Complete" : "Download Failed";
                        summaryMessageBuilder.AppendLine($"Succeeded: {downloadResult.SucceededItems.Count}, Failed: {downloadResult.FailedItems.Count}.");
                        summaryMessageBuilder.AppendLine("\nThe following items failed:");

                        foreach (var failedInfo in downloadResult.FailedItems)
                        {
                            summaryMessageBuilder.AppendLine($"- {failedInfo.Item.Name} ({failedInfo.Item.SteamId}): {failedInfo.Reason}");
                        }
                    }
                    else if (downloadResult.ExitCode != 0)
                    {
                        summaryTitle = "Download Failed";
                        summaryMessageBuilder.AppendLine($"SteamCMD process failed with Exit Code {downloadResult.ExitCode}. No items were processed.");
                        summaryMessageBuilder.AppendLine("Check the SteamCMD log files for more details.");
                    }
                    else
                    {
                        summaryTitle = "Download Finished with Errors";
                        summaryMessageBuilder.AppendLine("The download process finished with an unknown error. Check logs for details.");
                    }
                }

                string finalDialogSummary = summaryMessageBuilder.ToString();

                string statusBarMessage;
                if (token.IsCancellationRequested)
                {
                    statusBarMessage = "Download operation cancelled.";
                }
                else if (downloadResult.OverallSuccess)
                {
                    statusBarMessage = "Download complete.";
                }
                else
                {
                    statusBarMessage = $"Download finished with {downloadResult.FailedItems.Count} failure(s). See report for details.";
                }

                progressDialog.CompleteOperation(finalDialogSummary.Split('\n').FirstOrDefault() ?? "Finished.");
                StatusChanged?.Invoke(this, statusBarMessage);

                Debug.WriteLine($"[CommandHandler] Download Dialog Summary:\n{finalDialogSummary}");
                Debug.WriteLine($"[CommandHandler] Download Status Bar Message: {statusBarMessage}");

                if (downloadResult.FailedItems.Any())
                {
                    await _dialogService.ShowMessageWithCopy(summaryTitle, finalDialogSummary, MessageDialogType.Error);
                }
                else if (token.IsCancellationRequested || !downloadResult.OverallSuccess)
                {
                    await _dialogService.ShowWarning(summaryTitle, finalDialogSummary);
                }
                else
                {
                    await _dialogService.ShowInformation(summaryTitle, finalDialogSummary);
                }

                Debug.WriteLine("[CommandHandler] Download attempt finished, re-enriching remaining queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);

                if (refreshIsNeeded && !token.IsCancellationRequested)
                {
                    Debug.WriteLine("[CommandHandler] Download operation completed. Now signaling for UI refresh.");
                    DownloadCompletedAndRefreshNeeded?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Download operation cancelled by user.");
                progressDialog?.CompleteOperation("Download operation cancelled.");
                await _dialogService.ShowInformation("Cancelled", "Download operation cancelled. Partially processed items may exist.");
                Debug.WriteLine("[CommandHandler] Download cancelled, re-enriching queue items.");
                _modInfoEnricher.EnrichAllDownloadItems(_queueService.Items);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during download process: {ex.Message}");
                progressDialog?.CompleteOperation($"Error: {ex.Message}");
                Debug.WriteLine($"[ExecuteDownloadAsync] Exception: {ex}");
                await _dialogService.ShowError("Download Error", $"An unexpected error occurred during the download: {ex.Message}");
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
            if (!IsSteamCmdReady)
            {
                StatusChanged?.Invoke(this, "Cannot add mod: SteamCMD is not ready.");
                await _dialogService.ShowWarning("SteamCMD Not Ready", "SteamCMD setup needs to be completed before adding mods.");
                return;
            }

            token = CancellationTokenSource.CreateLinkedTokenSource(_getCancellationToken(), token).Token;

            OperationStarted?.Invoke(this, EventArgs.Empty);
            try
            {
                if (_browserViewModel.IsCollectionUrl)
                {
                    await AddItemsFromCollectionAsync(token);
                }
                else if (_browserViewModel.IsValidModUrl)
                {
                    await AddSingleModFromBrowserAsync(token);
                }
                else
                {
                    StatusChanged?.Invoke(this, "Cannot add mod: Current page is not a valid mod or collection page, or required info is missing.");
                    await _dialogService.ShowWarning("Cannot Add Mod", "The current browser page is not recognized as a valid mod or collection, or essential information couldn't be extracted.");
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
                await _dialogService.ShowError("Add Mod Error", $"An error occurred while trying to add the mod(s): {ex.Message}");
            }
            finally
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task AddSingleModFromBrowserAsync(CancellationToken token)
        {
            if (!_browserViewModel.IsModInfoAvailable)
            {
                string reason = "Mod information is not available. The mod may be deleted, unlisted, or the page hasn't loaded correctly.";
                await _dialogService.ShowError("Add Mod Failed", reason);
                StatusChanged?.Invoke(this, reason);
                return;
            }

            ModInfoDto? modInfo = null;
            string? failedReason = null;
            string? steamId = null;
            string? url = _browserViewModel.ActualCurrentUrl; 
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                steamId = queryParams["id"];

                if (string.IsNullOrEmpty(steamId))
                {
                    var pathMatch = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"/filedetails/(\d+)");
                    if (pathMatch.Success)
                    {
                        steamId = pathMatch.Groups[1].Value;
                    }
                }
            }
            try
            {
                StatusChanged?.Invoke(this, "Attempting to extract mod info from browser page...");
                modInfo = await _browserViewModel.GetCurrentModInfoAsync(token);
                token.ThrowIfCancellationRequested();
                if (modInfo != null)
                {
                    Debug.WriteLine($"[CommandHandler] Successfully extracted mod info via browser: {modInfo.Name} (ID: {modInfo.SteamId})");

                    if (string.IsNullOrEmpty(steamId) && !string.IsNullOrEmpty(modInfo.SteamId))
                    {
                        steamId = modInfo.SteamId;
                    }
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
                StatusChanged?.Invoke(this, "Browser extraction failed, trying Steam API fallback...");
            }
            if (string.IsNullOrEmpty(steamId))
            {
                try
                {
                    StatusChanged?.Invoke(this, "Trying to extract Steam ID from page content...");
                    var pageSteamId = await _browserViewModel.ExtractSteamIdFromPageAsync(token);
                    if (!string.IsNullOrEmpty(pageSteamId))
                    {
                        steamId = pageSteamId;
                        Debug.WriteLine($"[CommandHandler] Extracted Steam ID from page: {steamId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CommandHandler] Error extracting Steam ID from page: {ex.Message}");
                }
            }
            if (string.IsNullOrEmpty(steamId))
            {
                failedReason = "Could not extract Steam ID from the browser URL or page content. Please make sure you're on a valid Steam Workshop mod page.";
                await _dialogService.ShowError("Add Mod Failed", failedReason);
                StatusChanged?.Invoke(this, failedReason);
                return;
            }
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
                            Url = url,
                            PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                            StandardDate = apiUpdateTimeOffset.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                            FileSize = details.FileSize,
                            LatestVersions = SteamApiResultHelper.ExtractAndSortVersionTags(details.Tags)
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
            if (modInfo != null)
            {
                if (_queueService.AddToQueue(modInfo))
                {
                    Debug.WriteLine($"[CommandHandler] Added '{modInfo.Name}' to queue.");
                }
            }
            else
            {
                failedReason ??= "Could not retrieve mod information using browser extraction or Steam API.";
                await _dialogService.ShowError("Add Mod Failed", failedReason);
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
                await _dialogService.ShowWarning("Extraction Failed", "Could not find any items in the collection on the current page, or an error occurred during extraction.");
                return;
            }

            StatusChanged?.Invoke(this, $"Found {collectionItems.Count} items. Showing selection dialog...");
            var dialogViewModel = new CollectionDialogViewModel(collectionItems);
            List<string>? selectedIds = await _dialogService.ShowCollectionDialogAsync(dialogViewModel);

            token.ThrowIfCancellationRequested();

            if (selectedIds == null)
            {
                StatusChanged?.Invoke(this, "Collection add cancelled by user.");
                return;
            }

            if (!selectedIds.Any())
            {
                StatusChanged?.Invoke(this, "No items selected from the collection to add.");
                return;
            }
            StatusChanged?.Invoke(this, $"Adding {selectedIds.Count} selected item(s) to queue via Steam API check...");
            ProgressDialogViewModel? progressDialog = null;
            QueueProcessResult? processResult = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            try
            {
                progressDialog = _dialogService.ShowProgressDialog(
                    "Processing Collection Items",
                    "Starting API checks...",
                    canCancel: true,
                    isIndeterminate: false,
                    cts: linkedCts,
                    closeable: false
                );

                var progressReporter = new Progress<QueueProcessProgress>(p =>
                {
                    if (progressDialog?.CancellationToken.IsCancellationRequested ?? true) return;
                    RunOnUIThread(() =>
                    {
                        if (progressDialog != null)
                        {
                            progressDialog.Progress = (int)(((double)p.CurrentItem / p.TotalItems) * 100);
                            progressDialog.Message = $"Checking {p.CurrentItemName ?? p.CurrentSteamId}... ({p.CurrentItem}/{p.TotalItems})\n{p.Message}";
                        }
                        StatusChanged?.Invoke(this, $"Processing {p.CurrentItem}/{p.TotalItems}: {p.CurrentItemName ?? p.CurrentSteamId} - {p.Message}");
                    });
                });

                processResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(
                    selectedIds,
                    progressReporter,
                    linkedCts.Token
                );
                string summary;
                if (processResult.WasCancelled)
                {
                    summary = $"Processing cancelled. Successfully added {processResult.SuccessfullyAdded} mods before cancellation.";
                    progressDialog?.ForceClose();
                    await _dialogService.ShowInformation("Processing Cancelled", summary);
                }
                else
                {
                    summary = $"Processing complete. Attempted: {processResult.TotalAttempted}, Added: {processResult.SuccessfullyAdded}, Already Queued: {processResult.AlreadyQueued}, Failed: {processResult.FailedProcessing}.";
                    progressDialog?.CompleteOperation("Processing Complete.");

                    if (processResult.FailedProcessing > 0)
                    {
                        string errors = string.Join("\n- ", processResult.ErrorMessages.Take(5));
                        await _dialogService.ShowWarning("Processing Issues", $"{summary}\n\nSome items failed:\n- {errors}{(processResult.ErrorMessages.Count > 5 ? "\n..." : "")}");
                    }
                    else
                    {
                        await _dialogService.ShowInformation("Processing Complete", summary);
                    }
                }
                StatusChanged?.Invoke(this, summary);
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Collection item processing cancelled.");
                progressDialog?.ForceClose();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error processing collection items: {ex.Message}");
                progressDialog?.CompleteOperation($"Error: {ex.Message}");
                Debug.WriteLine($"[AddItemsFromCollectionAsync] Error: {ex}");
                await _dialogService.ShowError("Processing Error", $"An unexpected error occurred while processing collection items: {ex.Message}");
            }
            finally
            {
                progressDialog?.ForceClose();
            }
        }

        public async Task ExecuteRemoveItemAsync(DownloadItem? item)
        {
            if (item == null) return;

            var result = await _dialogService.ShowConfirmationAsync("Remove from Queue",
                $"Are you sure you want to remove '{item.Name}' from the queue?",
                showCancel: true);

            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            if (_queueService.RemoveFromQueue(item))
            {
                StatusChanged?.Invoke(this, $"Removed '{item.Name}' from the queue.");
            }
        }

        public async Task ExecuteNavigateToUrl(string? url)
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
                await _dialogService.ShowError("Navigation Error", $"Could not navigate the internal browser: {ex.Message}");
                Debug.WriteLine($"[CommandHandler] Error executing NavigateToUrl: {ex}");
            }
        }

        public async Task ExecuteRemoveItemsAsync(System.Collections.IList? selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0) return;

            var items = selectedItems.Cast<DownloadItem>().ToList();
            string confirmMessage = items.Count == 1
                ? $"Are you sure you want to remove '{items[0].Name}' from the queue?"
                : $"Are you sure you want to remove {items.Count} items from the queue?";

            var result = await _dialogService.ShowConfirmationAsync("Remove from Queue", confirmMessage, showCancel: true);
            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            int removedCount = 0;
            var removedNames = new List<string>();

            foreach (var item in items)
            {
                if (_queueService.RemoveFromQueue(item))
                {
                    removedCount++;
                    if (!string.IsNullOrEmpty(item.Name))
                    {
                        removedNames.Add(item.Name);
                    }
                }
            }

            if (removedCount > 0)
            {
                string message = removedCount == 1
                    ? $"Removed '{removedNames.FirstOrDefault() ?? "item"}' from the queue."
                    : $"Removed {removedCount} items from the queue.";

                StatusChanged?.Invoke(this, message);
            }
        }

        public async Task ExecuteCheckUpdatesAsync(CancellationToken token)
        {
            if (!IsSteamCmdReady)
            {
                StatusChanged?.Invoke(this, "Cannot check updates: SteamCMD is not ready.");
                await _dialogService.ShowWarning("SteamCMD Not Ready", "SteamCMD setup needs to be completed before checking for mod updates.");
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_getCancellationToken(), token);
            token = cts.Token;

            List<ModItem> workshopMods;
            ProgressDialogViewModel? gatherProgressDialog = null;

            OperationStarted?.Invoke(this, EventArgs.Empty);

            try
            {
                StatusChanged?.Invoke(this, "Gathering installed workshop mods...");

                gatherProgressDialog = _dialogService.ShowProgressDialog(
                    "Gathering Mods",
                    "Scanning installed workshop mods...",
                    canCancel: false, 
                    isIndeterminate: true,
                    closeable: false
                );
                await _modService.LoadModsAsync();
                workshopMods = _modService.GetLoadedMods()
                    .Where(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _) && m.ModType == ModType.WorkshopL)
                    .OrderBy(m => m.Name)
                    .ToList();

                gatherProgressDialog.ForceClose();
            }
            catch (Exception ex)
            {
                gatherProgressDialog?.ForceClose();
                OperationCompleted?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, $"Error loading mod list: {ex.Message}");
                Debug.WriteLine($"[CommandHandler] Error getting mods for update check: {ex}");
                await _dialogService.ShowError("Mod Load Error", $"Failed to load installed mods: {ex.Message}");
                return;
            }

            if (!workshopMods.Any())
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, "No installed Steam Workshop mods found to check.");
                await _dialogService.ShowInformation("Check Updates", "No installed Steam Workshop mods were found in your mods folder.");
                return;
            }

            var dialogViewModel = new UpdateCheckDialogViewModel(workshopMods);
            var dialogResult = await _dialogService.ShowUpdateCheckDialogAsync(dialogViewModel);

            if (dialogResult != RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck.UpdateCheckDialogResult.CheckUpdates)
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, "Update check cancelled via dialog.");
                return;
            }

            if (token.IsCancellationRequested) 
            { 
                OperationCompleted?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, "Update check cancelled."); 
                return; 
            }

            var selectedMods = dialogViewModel.GetSelectedMods().ToList();
            if (!selectedMods.Any())
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, "No mods were selected for update check.");
                return;
            }

            ProgressDialogViewModel? progressDialog = null;
            UpdateCheckResult updateResult = new();

            try
            {
                StatusChanged?.Invoke(this, $"Checking {selectedMods.Count} mod(s) for updates...");
                progressDialog = _dialogService.ShowProgressDialog("Checking for Updates", "Preparing...", true, true, cts, false);
                progressDialog.Cancelled += (s, e) => StatusChanged?.Invoke(this, "Update check cancelled via dialog.");

                var progress = new Progress<(int current, int total, string modName)>(update =>
                {
                    if (token.IsCancellationRequested) return;
                    if (progressDialog != null)
                    {
                        progressDialog.Message = $"Checking {update.modName}... ({update.current}/{update.total})";
                        progressDialog.IsIndeterminate = true;
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
                        await _dialogService.ShowWarning("Update Check Errors",
                            $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\n" +
                            $"Check status messages or logs for more context.\n\n" +
                            $"Errors Reported:\n • {allErrorsMessage}");
                    }

                    StatusChanged?.Invoke(this, summary);

                    if (updateResult.UpdatesFound > 0)
                    {
                        await _dialogService.ShowInformation("Updates Found", $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
                    }
                    else if (updateResult.ErrorsEncountered == 0)
                    {
                        await _dialogService.ShowInformation("No Updates Found", "All selected mods appear to be up to date.");
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
                await _dialogService.ShowError("Update Check Failed", $"An unexpected error occurred: {ex.Message}");
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
            if (_queueServiceStatusHandler != null && _queueService != null)
                _queueService.StatusChanged -= _queueServiceStatusHandler;
            if (_steamCmdSetupStateChangedHandler != null && _steamCmdService != null)
                _steamCmdService.SetupStateChanged -= _steamCmdSetupStateChangedHandler;
            _queueServiceStatusHandler = null;
            _steamCmdSetupStateChangedHandler = null;
            Debug.WriteLine("DownloadQueueCommandHandler cleaned up.");
        }
    }
}
