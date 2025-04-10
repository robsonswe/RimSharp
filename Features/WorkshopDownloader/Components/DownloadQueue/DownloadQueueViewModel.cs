#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Threading.Tasks;
using System.Diagnostics;
using RimSharp.Shared.Models;
using RimSharp.MyApp.Dialogs;
using System.Collections.Generic;
using System.Threading;
using RimSharp.Infrastructure.Workshop;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
// Ensure correct namespace for dialogs
using RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck;
using System.Collections.Specialized;

namespace RimSharp.Features.WorkshopDownloader.Components.DownloadQueue
{
    public class DownloadQueueViewModel : ViewModelBase
    {
        private readonly IDownloadQueueService _queueService;
        private readonly IModService _modService;
        private readonly IDialogService _dialogService;
        private readonly IWorkshopUpdateCheckerService _updateCheckerService;
        private readonly ISteamCmdService _steamCmdService;
        private readonly BrowserViewModel _browserViewModel; // <<< We already have this reference
        private readonly Func<CancellationToken> _getCancellationToken; // Function to get token from parent
        private readonly IModListManager _modListManager;
        private bool _isOperationInProgress; // Controlled by parent
        private Dictionary<string, ModItem> _localModLookupBySteamId = new();

        // To allow unsubscribing from lambda-based events
        private EventHandler<string>? _queueServiceStatusHandler;
        private NotifyCollectionChangedEventHandler? _queueServiceItemsHandler;

        // --- Properties for Binding ---
        public ObservableCollection<DownloadItem> DownloadList => _queueService.Items;
        public bool IsSteamCmdReady => _steamCmdService?.IsSetupComplete ?? false;
        public bool CanAddMod => _browserViewModel.IsValidModUrl && _browserViewModel.IsModInfoAvailable && !IsOperationInProgress;
        public bool CanDownload => DownloadList.Any() && IsSteamCmdReady && !IsOperationInProgress;

        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set
            {
                // Use SetProperty to handle change notification and avoid unnecessary updates
                if (SetProperty(ref _isOperationInProgress, value))
                {
                    RefreshCommandStates(); // Refresh commands when operation status changes
                }
            }
        }

        // --- Commands ---
        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand AddModCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand NavigateToUrlCommand { get; }

        // --- Events ---
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OperationStarted; // Notify parent
        public event EventHandler? OperationCompleted; // Notify parent
        public event EventHandler? DownloadCompletedAndRefreshNeeded; // Notify parent

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
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _updateCheckerService = updateCheckerService ?? throw new ArgumentNullException(nameof(updateCheckerService));
            _steamCmdService = steamCmdService ?? throw new ArgumentNullException(nameof(steamCmdService));
            _browserViewModel = browserViewModel ?? throw new ArgumentNullException(nameof(browserViewModel));
            _getCancellationToken = getCancellationToken ?? (() => CancellationToken.None); // Store delegate, provide default
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));

            // --- Subscribe to Service/Child VM Events ---
            _queueServiceStatusHandler = (s, msg) => StatusChanged?.Invoke(this, msg);
            _queueService.StatusChanged += _queueServiceStatusHandler;

            _queueServiceItemsHandler = QueueService_ItemsChanged;
            _queueService.Items.CollectionChanged += _queueServiceItemsHandler;

            _browserViewModel.ModInfoAvailabilityChanged += BrowserViewModel_ModInfoAvailabilityChanged;
            _browserViewModel.PropertyChanged += BrowserViewModel_PropertyChanged; // For IsValidModUrl etc.
            _steamCmdService.SetupStateChanged += SteamCmdService_SetupStateChanged;

            // --- Initialize Commands (using non-generic AsyncRelayCommand) ---
            SetupSteamCmdCommand = new AsyncRelayCommand(ExecuteSetupSteamCmdCommand, CanExecuteSetupSteamCmd);
            CheckUpdatesCommand = new AsyncRelayCommand(ExecuteCheckUpdatesCommand, CanExecuteCheckUpdates);
            DownloadCommand = new AsyncRelayCommand(ExecuteDownloadCommand, () => CanDownload);
            AddModCommand = new AsyncRelayCommand(ExecuteAddModCommand, () => CanAddMod);
            RemoveItemCommand = new RelayCommand(ExecuteRemoveItemCommand, _ => !IsOperationInProgress);
            NavigateToUrlCommand = new RelayCommand(ExecuteNavigateToUrlCommand, _ => !IsOperationInProgress); // <<< Keep RelayCommand here

            // --- Initial State ---
            Task.Run(UpdateSteamCmdReadyStatus); // Fire and forget
            // RefreshLocalModLookup(); // <<< Build initial lookup (Called within EnrichAll now)
            EnrichAllDownloadItems(); // <<< Enrich existing items (will refresh lookup first)
            RefreshCommandStates(); // Initial check
        }

        private void RefreshLocalModLookup()
        {
            try
            {
                // Use SteamID as the key, ignoring case for safety, filter out mods without SteamID
                var allMods = _modListManager.GetAllMods();
                Debug.WriteLine($"[QueueVM] Refreshing local mod lookup. Found {allMods.Count()} total mods reported by manager.");

                _localModLookupBySteamId = allMods
                    .Where(m => !string.IsNullOrEmpty(m.SteamId))
                    .GroupBy(m => m.SteamId, StringComparer.OrdinalIgnoreCase) // Group to handle potential duplicates (take first)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                Debug.WriteLine($"[QueueVM] Refreshed local mod lookup, stored {_localModLookupBySteamId.Count} mods with SteamIDs.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QueueVM] Error refreshing local mod lookup: {ex.Message}");
                _localModLookupBySteamId.Clear(); // Ensure lookup is empty on error
            }
        }

        // --- NEW METHOD: Enrich a single DownloadItem ---
        private void EnrichDownloadItem(DownloadItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.SteamId)) return;

            if (_localModLookupBySteamId.TryGetValue(item.SteamId, out var localMod))
            {
                 Debug.WriteLine($"[QueueVM] Enriching item '{item.Name}' ({item.SteamId}): Found local match.");
                // Found the mod locally
                item.IsInstalled = true;
                item.LocalDateStamp = localMod.DateStamp; // Assuming ModItem has DateStamp property
                item.IsActive = _modListManager.IsModActive(localMod);
                item.IsLocallyOutdatedRW = localMod.IsOutdatedRW; // Assuming ModItem has IsOutdatedRW
            }
            else
            {
                Debug.WriteLine($"[QueueVM] Enriching item '{item.Name}' ({item.SteamId}): No local match found.");
                // Mod not found locally
                item.ClearLocalInfo(); // Use helper to reset properties
            }
        }

        // --- MODIFIED METHOD: Enrich all items in the list ---
        private void EnrichAllDownloadItems()
        {
            RefreshLocalModLookup(); // <<< Ensure lookup is fresh before enriching all
            Debug.WriteLine($"[QueueVM] Enriching all {DownloadList.Count} download items using refreshed lookup...");
            foreach (var item in DownloadList)
            {
                EnrichDownloadItem(item);
            }
            Debug.WriteLine($"[QueueVM] Enrichment complete.");
        }

        // --- MODIFIED Event Handler ---
        private void QueueService_ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RunOnUIThread(() =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    // *** FIX: Refresh the lookup BEFORE enriching new items ***
                    RefreshLocalModLookup();

                    // Enrich only the newly added items
                    Debug.WriteLine($"[QueueVM] Items added, enriching {e.NewItems.Count} new item(s) using refreshed lookup...");
                    foreach (DownloadItem newItem in e.NewItems)
                    {
                        EnrichDownloadItem(newItem);
                    }
                }
                else if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    // If the whole list was cleared, maybe refresh lookup? Not strictly necessary unless local mods changed too.
                    Debug.WriteLine("[QueueVM] Download list reset.");
                    // When list is reset, enrichment happens on load or manual refresh.
                }
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                     Debug.WriteLine("[QueueVM] Item removed from download list.");
                     // No enrichment needed, just update commands potentially
                }

                // Always refresh potentially affected properties/commands
                OnPropertyChanged(nameof(DownloadList)); // Affects UI list itself
                OnPropertyChanged(nameof(CanDownload));
                ((AsyncRelayCommand)DownloadCommand).RaiseCanExecuteChanged();
            });
        }

        public void RefreshLocalModInfo()
        {
            RunOnUIThread(() =>
            {
                Debug.WriteLine("[QueueVM] External request to refresh local mod info.");
                // RefreshLocalModLookup(); // Already done by EnrichAllDownloadItems
                EnrichAllDownloadItems();
            });
        }


        private void BrowserViewModel_ModInfoAvailabilityChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(CanAddMod));
                ((AsyncRelayCommand)AddModCommand).RaiseCanExecuteChanged();
            });
        }

        private void BrowserViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // React to IsValidModUrl changes from BrowserViewModel
            if (e.PropertyName == nameof(BrowserViewModel.IsValidModUrl))
            {
                RunOnUIThread(() =>
                {
                    OnPropertyChanged(nameof(CanAddMod));
                    ((AsyncRelayCommand)AddModCommand).RaiseCanExecuteChanged();
                });
            }
            // Potentially react to other properties if needed
        }

        private void SteamCmdService_SetupStateChanged(object? sender, bool isSetup)
        {
            RunOnUIThread(() =>
            {
                OnPropertyChanged(nameof(IsSteamCmdReady));
                OnPropertyChanged(nameof(CanDownload));
                ((AsyncRelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged(); // CanExecute depends on IsOperationInProgress
            });
        }

        // Central method to refresh command states, ensures UI thread
        public void RefreshCommandStates()
        {
            RunOnUIThread(() =>
            {
                ((AsyncRelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)AddModCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RemoveItemCommand).RaiseCanExecuteChanged();
                ((RelayCommand)NavigateToUrlCommand).RaiseCanExecuteChanged();
                // Explicitly update computed properties bound to UI
                OnPropertyChanged(nameof(CanAddMod));
                OnPropertyChanged(nameof(CanDownload));
            });
        }

        // --- Command CanExecute Methods (Omitted for brevity - no changes here) ---
        private bool CanExecuteSetupSteamCmd() => !IsOperationInProgress;
        private bool CanExecuteCheckUpdates()
        {
            // Defensively check if mod service is available and has loaded mods
            try { return !IsOperationInProgress && (_modService?.GetLoadedMods().Any(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _)) ?? false); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking CanExecuteCheckUpdates: {ex.Message}");
                return false;
            }
        }

        // --- Command Execute Methods ---

        // ExecuteSetupSteamCmdCommand, ExecuteDownloadCommand, ExecuteAddModCommand (Omitted for brevity - no changes here)
        private async Task ExecuteSetupSteamCmdCommand(CancellationToken ignoredToken)
        {
            if (IsOperationInProgress) return;
            CancellationToken token = _getCancellationToken(); // Get token from parent
            if (token.IsCancellationRequested) return;

            OperationStarted?.Invoke(this, EventArgs.Empty); // Notify parent operation started
            ProgressDialogViewModel? progressDialog = null;
            try
            {
                progressDialog = _dialogService.ShowProgressDialog("SteamCMD Setup", "Starting setup...", true); // Can Cancel
                // Link dialog's cancel request to the parent's token source (parent VM should handle this linkage)
                // For simplicity, we assume parent cancellation is signaled via the token.
                // A more direct link: progressDialog.Cancelled += (s,e) => _parentViewModel.CancelOperation();
                progressDialog.Cancelled += (s, e) => { /* Parent handles cancellation request */ };

                var progressReporter = new Progress<string>(message =>
                {
                    if (progressDialog != null) progressDialog.Message = message;
                    StatusChanged?.Invoke(this, message); // Update status bar
                });

                StatusChanged?.Invoke(this, "Running SteamCMD Setup...");
                bool success = await _steamCmdService.SetupAsync(progressReporter, token); // Pass the actual token

                if (token.IsCancellationRequested)
                {
                    progressDialog?.OnCancel("Setup cancelled by user.");
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
                    progressDialog?.OnCancel("Setup failed. See log/messages.");
                    StatusChanged?.Invoke(this, "SteamCMD setup failed.");
                    _dialogService.ShowError("Setup Failed", "SteamCMD setup failed. Please check the logs or status messages for details.");
                }
            }
            catch (OperationCanceledException)
            {
                progressDialog?.OnCancel("Setup cancelled by user.");
                StatusChanged?.Invoke(this, "SteamCMD setup cancelled.");
                // No need for dialog here, cancellation is expected
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during SteamCMD setup: {ex.Message}");
                progressDialog?.OnCancel($"Error: {ex.Message}");
                _dialogService.ShowError("Setup Error", $"An unexpected error occurred during setup: {ex.Message}");
                Debug.WriteLine($"[ExecuteSetupSteamCmdCommand] Error: {ex}");
            }
            finally
            {
                await UpdateSteamCmdReadyStatus(); // Refresh IsSteamCmdReady property
                progressDialog?.ForceClose(); // Ensure dialog is closed
                OperationCompleted?.Invoke(this, EventArgs.Empty); // Notify parent operation finished
            }
        }

        private async Task ExecuteDownloadCommand(CancellationToken ignoredToken)
        {
            if (IsOperationInProgress || !DownloadList.Any()) return;
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

            var itemsToDownload = DownloadList.ToList(); // Snapshot the queue
            SteamCmdDownloadResult? downloadResult = null;
            bool refreshIsNeeded = false;
            bool localInfoRefreshNeeded = false;

            try
            {
                StatusChanged?.Invoke(this, $"Starting download of {itemsToDownload.Count} mod(s). Please observe the SteamCMD window...");

                // Execute SteamCMD download
                downloadResult = await _steamCmdService.DownloadModsAsync(itemsToDownload, false, token); // Pass actual token

                if (token.IsCancellationRequested) throw new OperationCanceledException();

                StatusChanged?.Invoke(this, "SteamCMD process finished. Processing results...");
                Debug.WriteLine($"SteamCMD finished. Overall Success: {downloadResult.OverallSuccess}, Exit Code: {downloadResult.ExitCode}");

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
                            refreshIsNeeded = true;
                            localInfoRefreshNeeded = true; // <<< Mark that local info *definitely* changed
                            // Remove from the queue ON THE UI THREAD
                            RunOnUIThread(() => _queueService.RemoveFromQueue(successItem));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException) // Don't treat cancellation as a processing error
                        {
                            Debug.WriteLine($"Error post-processing success item {successItem.Name} ({successItem.SteamId}): {ex.Message}");
                            failCount++; // Count this as failure if post-processing fails
                            StatusChanged?.Invoke(this, $"Error processing {successItem.Name}. It remains in the queue.");
                            // Consider *not* removing item from queue if post-processing fails? Current logic removes if download succeeded.
                        }
                    }
                }

                // --- Final Reporting ---
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
                if (localInfoRefreshNeeded) // Refresh local info if any success or potential change occurred
                {
                    // Even if cancelled, some items might have completed post-processing
                    Debug.WriteLine("[QueueVM] Local mod info potentially changed, triggering refresh.");
                    // Don't call RefreshLocalModInfo directly here if the parent handles it via the event
                    // It *will* be called by the parent via the DownloadCompletedAndRefreshNeeded handler flow
                }
                else
                {
                     // Maybe still refresh failed items? They might have become installed via other means.
                     // Let's enrich *all* items again just to be sure the display is consistent
                     // after the operation, even if no downloads succeeded.
                     Debug.WriteLine("[QueueVM] No successful downloads, but re-enriching queue items for consistency.");
                     EnrichAllDownloadItems();
                }


                if (refreshIsNeeded && !token.IsCancellationRequested)
                {
                    Debug.WriteLine("[QueueVM] Download completed with successes, requesting parent refresh.");
                    DownloadCompletedAndRefreshNeeded?.Invoke(this, EventArgs.Empty);
                    // The event handler in DownloaderViewModel will trigger QueueViewModel.RefreshLocalModInfo()
                }

            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Download operation cancelled by user.");
                _dialogService.ShowInformation("Cancelled", "Download operation cancelled. Partially downloaded items may exist but were not fully processed.");
                 // Even on cancellation, re-enrich to reflect any partial successes before cancel took effect
                 Debug.WriteLine("[QueueVM] Download cancelled, re-enriching queue items.");
                 EnrichAllDownloadItems();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error during download process: {ex.Message}");
                Debug.WriteLine($"[ExecuteDownloadCommand] Exception: {ex}");
                _dialogService.ShowError("Download Error", $"An unexpected error occurred during the download: {ex.Message}");
                 // On error, re-enrich to ensure queue reflects current known state
                 Debug.WriteLine("[QueueVM] Download error occurred, re-enriching queue items.");
                 EnrichAllDownloadItems();
            }
            finally
            {
                OperationCompleted?.Invoke(this, EventArgs.Empty); // Notify parent operation finished
            }
        }

        private async Task ExecuteAddModCommand(CancellationToken ignoredToken)
        {
            if (IsOperationInProgress || !CanAddMod) return;
            CancellationToken token = _getCancellationToken();
            if (token.IsCancellationRequested) return;

            StatusChanged?.Invoke(this, "Extracting mod info from browser...");
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
                    // AddToQueue will trigger CollectionChanged -> QueueService_ItemsChanged -> RefreshLocalModLookup -> EnrichDownloadItem
                    if (_queueService.AddToQueue(modInfo))
                    {
                        // Use correct property name 'Name'
                        // Status update is handled by AddToQueue and ItemsChanged->Enrich now
                        // StatusChanged?.Invoke(this, $"Added '{modInfo.Name}' to queue."); // Redundant
                    }
                }
                else
                {
                    StatusChanged?.Invoke(this, "Could not extract mod info from the current page.");
                    // Optionally show a dialog if this is unexpected
                    // _dialogService.ShowWarning("Extraction Failed", "Could not get mod details from the current page. Ensure it's a valid Steam Workshop item page.");
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
            finally
            {
                // No OperationStarted/Completed needed for this quick action unless extraction is slow
            }
        }


        private void ExecuteRemoveItemCommand(object? parameter)
        {
            if (IsOperationInProgress) return; // Don't modify queue during other operations

            if (parameter is DownloadItem item)
            {
                _queueService.RemoveFromQueue(item); // Service handles status update/logging
                // Status update handled by service now
                // StatusChanged?.Invoke(this, $"Removed '{item.Name}' from queue.");
            }
        }

        private void ExecuteNavigateToUrlCommand(object? url)
        {
            if (IsOperationInProgress) return; // Don't navigate during other operations

            if (url is string urlString && !string.IsNullOrEmpty(urlString))
            {
                // Use the BrowserViewModel's navigation command
                try
                {
                    Debug.WriteLine($"[QueueVM] Requesting navigation to: {urlString}");
                    // Check if the command can execute (it checks !IsOperationInProgress internally)
                    if (_browserViewModel.NavigateToUrlCommand.CanExecute(urlString))
                    {
                        _browserViewModel.NavigateToUrlCommand.Execute(urlString);
                        StatusChanged?.Invoke(this, $"Navigating browser to: {urlString}");
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "Browser cannot navigate now (possibly busy).");
                        Debug.WriteLine($"[QueueVM] BrowserViewModel.NavigateToUrlCommand cannot execute for {urlString}");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Failed to initiate browser navigation: {ex.Message}");
                    _dialogService.ShowError("Navigation Error", $"Could not navigate the internal browser: {ex.Message}");
                    Debug.WriteLine($"Error executing NavigateToUrlCommand via BrowserVM: {ex}");
                }
            }
            else
            {
                StatusChanged?.Invoke(this, "Invalid URL provided for navigation.");
            }
        }

        // ExecuteCheckUpdatesCommand (Omitted for brevity - no changes here)
        private async Task ExecuteCheckUpdatesCommand(CancellationToken ignoredToken)
        {
            if (IsOperationInProgress) return;
            CancellationToken token = _getCancellationToken();
            if (token.IsCancellationRequested) return;

            List<ModItem> workshopMods;
            try
            {
                StatusChanged?.Invoke(this, "Gathering installed workshop mods...");
                 // Refresh local mod info just before check, ensuring IModService is up-to-date
                await _modService.LoadModsAsync(); // Assuming LoadModsAsync refreshes the list used by GetLoadedMods
                workshopMods = _modService.GetLoadedMods()
                   .Where(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _))
                   .OrderBy(m => m.Name)
                   .ToList();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading mod list: {ex.Message}");
                Debug.WriteLine($"Error getting mods for update check: {ex}");
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
            var dialogResult = _dialogService.ShowUpdateCheckDialog(dialogViewModel);

            if (dialogResult != Dialogs.UpdateCheck.UpdateCheckDialogResult.CheckUpdates)
            {
                StatusChanged?.Invoke(this, "Update check cancelled.");
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
                progressDialog = _dialogService.ShowProgressDialog("Checking for Updates", "Preparing...", true); // Can Cancel
                progressDialog.Cancelled += (s, e) => { /* Parent handles cancellation */ };

                // Define progress reporting action
                var progress = new Progress<(int current, int total, string modName)>(update =>
                {
                    if (progressDialog != null) // Check if dialog still exists and isn't cancelled
                    {
                        progressDialog.Message = $"Checking {update.modName}... ({update.current}/{update.total})";
                        progressDialog.Progress = (int)((double)update.current / update.total * 100);
                        progressDialog.IsIndeterminate = false;
                    }
                    // Update status bar regardless of dialog state
                    StatusChanged?.Invoke(this, $"Checking {update.modName} ({update.current} of {update.total})...");
                });

                // Perform the update check
                updateResult = await _updateCheckerService.CheckForUpdatesAsync(selectedMods, progress, token); // Pass actual token

                // --- Report Results (only if not cancelled) ---
                if (token.IsCancellationRequested)
                {
                    progressDialog?.OnCancel("Update check cancelled by user.");
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
                        var errorSample = updateResult.ErrorMessages.Take(3).ToList();
                        var errorMessage = string.Join("\n", errorSample);
                        if (updateResult.ErrorMessages.Count > 3) { errorMessage += $"\n(and {updateResult.ErrorMessages.Count - 3} more errors...)"; }
                        _dialogService.ShowWarning("Update Check Errors", $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\nCheck status messages or logs for details.\n\nSample:\n{errorMessage}");
                    }

                    StatusChanged?.Invoke(this, summary); // Update status bar with summary

                    // Show appropriate final dialog
                    if (updateResult.UpdatesFound > 0)
                    {
                        _dialogService.ShowInformation("Updates Found", $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
                        // The AddToQueue calls within the update checker will trigger enrichment via CollectionChanged
                    }
                    else if (updateResult.ErrorsEncountered == 0) // Only show "no updates" if no errors occurred
                    {
                        _dialogService.ShowInformation("No Updates Found", "All selected mods are up to date.");
                    }
                    // If only errors occurred, the warning dialog about errors serves as notification

                    // Re-enrich the entire list *after* the update check, in case mods were added to the queue
                    Debug.WriteLine("[QueueVM] Update check finished, re-enriching queue items.");
                    EnrichAllDownloadItems();
                }
            }
            catch (OperationCanceledException)
            {
                // Catch cancellation specifically if needed (e.g., user cancelled via dialog)
                progressDialog?.OnCancel("Update check was cancelled.");
                StatusChanged?.Invoke(this, "Update check was cancelled.");
                // No dialog needed here usually
            }
            catch (Exception ex)
            {
                progressDialog?.OnCancel($"Error: {ex.Message}"); // Show error in progress dialog before closing
                StatusChanged?.Invoke(this, $"An error occurred during the update check process: {ex.Message}");
                Debug.WriteLine($"Error executing update check: {ex}");
                _dialogService.ShowError("Update Check Failed", $"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                progressDialog?.ForceClose(); // Ensure closure
                OperationCompleted?.Invoke(this, EventArgs.Empty); // Notify parent
            }
        }


        // Helper method to fetch SteamCMD status
        private async Task UpdateSteamCmdReadyStatus()
        {
            await _steamCmdService.CheckSetupAsync();
            // State change notification is handled by SteamCmdService_SetupStateChanged event handler
        }

        // Clean up event subscriptions
        public void Cleanup()
        {
            // Unsubscribe from all events to prevent memory leaks
            if (_queueServiceStatusHandler != null)
                _queueService.StatusChanged -= _queueServiceStatusHandler;
            if (_queueServiceItemsHandler != null)
                _queueService.Items.CollectionChanged -= _queueServiceItemsHandler;

            _browserViewModel.ModInfoAvailabilityChanged -= BrowserViewModel_ModInfoAvailabilityChanged;
            _browserViewModel.PropertyChanged -= BrowserViewModel_PropertyChanged;
            _steamCmdService.SetupStateChanged -= SteamCmdService_SetupStateChanged;

            // Clear handlers to be safe
            _queueServiceStatusHandler = null;
            _queueServiceItemsHandler = null;

            Debug.WriteLine("DownloadQueueViewModel cleaned up.");
        }
    }
}