using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models; // For ModInfoDto
using System.Globalization; // For CultureInfo

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel : ViewModelBase // Ensure inherits from ViewModelBase
    {
        // Dependencies (Remain here)
        private readonly IModDataService _dataService;
        private readonly IModCommandService _commandService;
        private readonly IModListIOService _ioService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IModIncompatibilityService _incompatibilityService;
        private readonly IPathService _pathService;

        private readonly IModService _modService;

        private readonly IModReplacementService _replacementService;
        private readonly IDownloadQueueService _downloadQueueService;
        private readonly ISteamApiClient _steamApiClient;
        private readonly IApplicationNavigationService _navigationService;
        // State properties (Remain here)
        private bool _isParentLoading;
        private bool _hasUnsavedChanges;
        private ModItem _selectedMod; // For single-item actions
        private IList _selectedItems; // For multi-item actions
        protected bool CanExecuteSimpleCommands() => !IsParentLoading && HasValidPaths;

        public bool IsParentLoading
        {
            get => _isParentLoading;
            set
            {
                Debug.WriteLine($"[ModActionsViewModel] IsParentLoading SETTER: Current = {_isParentLoading}, New = {value}");
                // Use base SetProperty, command observation handles updates
                if (SetProperty(ref _isParentLoading, value))
                {
                    Debug.WriteLine($"[ModActionsViewModel] IsParentLoading Changed to {value}.");
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                // Use base SetProperty, command observation handles updates
                // REMOVE THE EVENT INVOCATION FROM HERE
                // if (SetProperty(ref _hasUnsavedChanges, value))
                // {
                //     // Request update in parent VM if needed (handled by event)
                //     // HasUnsavedChangesRequest?.Invoke(this, value); // <<< REMOVE THIS LINE
                // }
                // Setter still needs to raise PropertyChanged for command observation:
                SetProperty(ref _hasUnsavedChanges, value);
            }
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set
            {
                // Use base SetProperty, command observation handles updates
                SetProperty(ref _selectedMod, value);
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        public IList SelectedItems // Bound from ListBox typically
        {
            get => _selectedItems;
            set
            {
                // Use base SetProperty, command observation handles updates
                SetProperty(ref _selectedItems, value);
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        private bool _hasValidPaths;
        public bool HasValidPaths
        {
            get => _hasValidPaths;
            private set => SetProperty(ref _hasValidPaths, value);
        }


        // Command Properties (Declarations remain here)
        // List Management
        public ICommand ClearActiveListCommand { get; private set; }
        public ICommand SortActiveListCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand ImportListCommand { get; private set; }
        public ICommand ExportListCommand { get; private set; }
        public ICommand CheckReplacementsCommand { get; private set; }

        // Mod Actions (Single/Multi)
        public ICommand DeleteModCommand { get; private set; } // Single
        public ICommand DeleteModsCommand { get; private set; } // Multi
        public ICommand OpenModFoldersCommand { get; private set; } // Multi
        public ICommand OpenUrlsCommand { get; private set; } // Multi
        public ICommand OpenWorkshopPagesCommand { get; private set; } // Multi
        public ICommand OpenOtherUrlsCommand { get; private set; } // Multi

        // Tools & Analysis
        public ICommand ResolveDependenciesCommand { get; private set; }
        public ICommand CheckIncompatibilitiesCommand { get; private set; }
        public ICommand CheckDuplicatesCommand { get; private set; }

        // Placeholders
        public ICommand StripModsCommand { get; private set; }
        public ICommand FixIntegrityCommand { get; private set; }
        public ICommand RunGameCommand { get; private set; }
        public ICommand CustomizeModCommand { get; private set; }

        // Installation
        public ICommand InstallFromZipCommand { get; private set; }
        public ICommand InstallFromGithubCommand { get; private set; }
        public ICommand RedownloadModsCommand { get; private set; }

        // Events (Remain here)
        public event EventHandler<bool> IsLoadingRequest;
        public event EventHandler RequestDataRefresh;
        public event EventHandler<bool> HasUnsavedChangesRequest;

        // Helper state (Example, might be better encapsulated if complex)
        private bool _installSuccess = false;

        // Constructor (Remains here)
        public ModActionsViewModel(
            IModDataService dataService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService,
            IPathService pathService,
            IModService modService,
            IModReplacementService replacementService,
            IDownloadQueueService downloadQueueService,
            ISteamApiClient steamApiClient,
            IApplicationNavigationService navigationService)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;
            _pathService = pathService;
            _modService = modService;
            _replacementService = replacementService;
            _pathService.RefreshPaths();
            _downloadQueueService = downloadQueueService;
            _steamApiClient = steamApiClient;
            _navigationService = navigationService;
            RefreshPathValidity();
            InitializeCommands(); // Calls partial initialization methods
        }

        // Combined initializer calling partial initializers
        private void InitializeCommands()
        {
            InitializeListManagementCommands();
            InitializeModActionsCommands();
            InitializeToolsAnalysisCommands();
            InitializeInstallationCommands();
            InitializePlaceholderCommands();
            CustomizeModCommand = CreateAsyncCommand<ModItem>(
                execute: ExecuteCustomizeMod,
                canExecute: CanExecutizeMod,
                observedProperties: new[] { nameof(IsParentLoading), nameof(SelectedMod) });
            InitializeRedownloadCommand(); // Call the new initializer
        }

        private void InitializeRedownloadCommand()
        {
            RedownloadModsCommand = CreateCancellableAsyncCommand( // Use the NON-GENERIC version
                execute: async (ct) => // Lambda takes CancellationToken
                {
                    // Get the selected items from the ViewModel property *at execution time*
                    var itemsToProcess = SelectedItems;
                    if (itemsToProcess != null && itemsToProcess.Count > 0) // Add a null/empty check for safety
                    {
                        await ExecuteRedownloadModsAsync(itemsToProcess, ct);
                    }
                    else
                    {
                        Debug.WriteLine("[RedownloadModsCommand] Execute called but SelectedItems was null or empty.");
                    }
                },
                canExecute: () => // Lambda takes no parameters
                {
                    // Get the selected items from the ViewModel property *at evaluation time*
                    var currentSelection = SelectedItems;
                    // Call the original CanExecute logic using the retrieved items
                    return CanExecuteRedownloadMods(currentSelection);
                },
                // Observation still works correctly on the ViewModel's properties
                observedProperties: new[] { nameof(SelectedItems), nameof(IsParentLoading) });
        }
        private bool CanExecuteRedownloadMods(IList selectedItems)
        {
            var currentSelection = this.SelectedItems; // Read the property value for consistency

            bool isLoading = IsParentLoading;
            int count = currentSelection?.Count ?? 0;
            bool hasSelection = currentSelection != null && count > 0;

            Debug.WriteLine($"[CanExecuteRedownloadMods] Check: IsLoading={isLoading}, VM.SelectedItems.Count={count}");

            if (isLoading || !hasSelection)
            {
                Debug.WriteLine($"[CanExecuteRedownloadMods] Result: false (Loading or No Selection)");
                return false;
            }

            // --- CHANGE HERE: Use Any() instead of All() ---
            // Check if AT LEAST ONE selected item is a valid WorkshopL mod with a Steam ID
            bool anyValid = false;
            try
            {
                anyValid = currentSelection.Cast<ModItem>().ToList().Any(mod => // <-- Changed from All to Any
                    mod != null &&
                    mod.ModType == ModType.WorkshopL &&
                    !string.IsNullOrEmpty(mod.SteamId) &&
                    long.TryParse(mod.SteamId, out _)
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CanExecuteRedownloadMods] Error during Any() check: {ex.Message}");
                anyValid = false; // Treat error as invalid
            }
            // --- END CHANGE ---

            Debug.WriteLine($"[CanExecuteRedownloadMods] AnyValid Check Result: {anyValid}. Final CanExecute: {anyValid}");
            return anyValid; // Command is executable if at least one valid item exists
        }

        private async Task ExecuteRedownloadModsAsync(IList selectedItems, CancellationToken ct)
        {
            var currentSelection = selectedItems ?? SelectedItems;

            // --- STEP 1: Filter the list FIRST ---
            var modsToRedownload = currentSelection?.Cast<ModItem>()
                .Where(mod => mod != null && mod.ModType == ModType.WorkshopL && !string.IsNullOrEmpty(mod.SteamId))
                .ToList();

            // --- STEP 2: Check if any valid mods remain AFTER filtering ---
            if (modsToRedownload == null || !modsToRedownload.Any())
            {
                // This case should ideally not happen if CanExecute uses Any(), but good safety check.
                Debug.WriteLine("[ExecuteRedownloadModsAsync] No valid WorkshopL mods found in the selection after filtering.");
                _dialogService.ShowInformation("Redownload Mod", "No selected mods are eligible for redownload (must be locally installed Workshop mods).");
                return;
            }

            // --- STEP 3: Generate confirmation based on the FILTERED list ---
            string confirmMessage = modsToRedownload.Count == 1
                ? $"Are you sure you want to queue '{modsToRedownload.First().Name}' for redownload?\n\nThis will add the mod to the download queue. It will *not* check if an update is available."
                : $"Are you sure you want to queue {modsToRedownload.Count} Workshop mod(s) for redownload?\n\nThis will add the selected mods to the download queue. It will *not* check if updates are available.";

            var confirmResult = _dialogService.ShowConfirmation("Confirm Redownload", confirmMessage, showCancel: true);
            if (confirmResult != MessageDialogResult.OK && confirmResult != MessageDialogResult.Yes)
            {
                Debug.WriteLine("[ExecuteRedownloadModsAsync] User cancelled redownload confirmation.");
                return;
            }

            // --- STEP 4: Proceed with the execution using the FILTERED list ---
            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel progressDialog = null;
            CancellationTokenSource linkedCts = null;

            int addedCount = 0;
            int alreadyQueuedCount = 0;
            int errorCount = 0;
            var apiErrorMessages = new List<string>();
            var addedNames = new List<string>();

            try
            {
                // Show progress dialog
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                       "Queueing Mods for Redownload",
                       "Checking mods with Steam API...",
                       canCancel: true,
                       isIndeterminate: false, // Determinate progress
                       cts: null, // Will create linked CTS below
                       closeable: true);
                });

                if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                for (int i = 0; i < modsToRedownload.Count; i++)
                {
                    combinedToken.ThrowIfCancellationRequested();
                    var mod = modsToRedownload[i];
                    var steamId = mod.SteamId; // Already validated

                    // Update progress
                    string progressMessage = $"Checking '{mod.Name}' ({steamId})... ({i + 1}/{modsToRedownload.Count})";
                    await RunOnUIThreadAsync(() =>
                    {
                        if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                        {
                            progressDialog.Message = progressMessage;
                            progressDialog.Progress = (int)((double)(i + 1) / modsToRedownload.Count * 100);
                        }
                    });

                    // Check if already in queue
                    if (_downloadQueueService.IsInQueue(steamId))
                    {
                        Debug.WriteLine($"[ExecuteRedownloadModsAsync] Mod '{mod.Name}' ({steamId}) is already in the download queue.");
                        alreadyQueuedCount++;
                        continue;
                    }

                    // --- Steam API Check ---
                    SteamApiResponse apiResponse = null;
                    try
                    {
                        apiResponse = await _steamApiClient.GetFileDetailsAsync(steamId, combinedToken);

                        if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
                        {
                            var errorMsg = $"No details returned from Steam API for '{mod.Name}' ({steamId}).";
                            Debug.WriteLine($"[ExecuteRedownloadModsAsync] {errorMsg}");
                            apiErrorMessages.Add(errorMsg);
                            errorCount++;
                            continue;
                        }

                        var details = apiResponse.Response.PublishedFileDetails.First();
                        if (details.Result != 1) // 1 = OK
                        {
                            string errorDescription = SteamApiResultHelper.GetDescription(details.Result);
                            var errorMsg = $"Steam API error for '{mod.Name}' ({steamId}): {errorDescription} (Code: {details.Result})";
                            Debug.WriteLine($"[ExecuteRedownloadModsAsync] {errorMsg}");
                            apiErrorMessages.Add(errorMsg);
                            errorCount++;
                            continue;
                        }

                        // --- Add to Queue ---
                        // No date comparison needed here
                        DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
                        DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;

                        var modInfoDto = new ModInfoDto
                        {
                            Name = details.Title ?? mod.Name, // Prefer API title
                            SteamId = steamId,
                            Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamId}",
                            PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                            StandardDate = apiUpdateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                        };

                        if (_downloadQueueService.AddToQueue(modInfoDto))
                        {
                            addedCount++;
                            addedNames.Add(modInfoDto.Name);
                            Debug.WriteLine($"[ExecuteRedownloadModsAsync] Added '{modInfoDto.Name}' ({modInfoDto.SteamId}) to download queue.");
                        }
                        else
                        {
                            // AddToQueue returned false (e.g., internal failure?)
                            var errorMsg = $"Failed to add '{modInfoDto.Name}' ({modInfoDto.SteamId}) to queue internally.";
                            Debug.WriteLine($"[ExecuteRedownloadModsAsync] {errorMsg}");
                            apiErrorMessages.Add(errorMsg); // Log it as an error
                            errorCount++;
                        }
                    }
                    catch (OperationCanceledException) { throw; } // Re-throw cancellation
                    catch (Exception apiEx)
                    {
                        var errorMsg = $"Error checking Steam API for '{mod.Name}' ({steamId}): {apiEx.Message}";
                        Debug.WriteLine($"[ExecuteRedownloadModsAsync] {errorMsg}");
                        apiErrorMessages.Add(errorMsg);
                        errorCount++;
                        // Continue with the next item
                    }
                } // --- End For Loop ---

                // --- Show Summary ---
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog?.CompleteOperation("Redownload queueing complete.");

                    var sb = new StringBuilder();
                    if (addedCount > 0) sb.AppendLine($"{addedCount} mod(s) added to the download queue.");
                    else sb.AppendLine("No new mods were added to the download queue.");

                    if (alreadyQueuedCount > 0) sb.AppendLine($"{alreadyQueuedCount} selected mod(s) were already in the queue.");
                    if (errorCount > 0)
                    {
                        sb.AppendLine($"{errorCount} selected mod(s) could not be added due to errors:");
                        foreach (var errMsg in apiErrorMessages.Take(5)) // Show first few errors
                        {
                            sb.AppendLine($"  - {errMsg}");
                        }
                        if (apiErrorMessages.Count > 5) sb.AppendLine("    (Check debug logs for more...)");
                    }

                    // Show appropriate dialog
                    if (errorCount > 0 && addedCount > 0) // Partial success
                    {
                        _dialogService.ShowWarning("Redownload Partially Queued", sb.ToString().Trim());
                    }
                    else if (errorCount > 0 && addedCount == 0) // All failed or skipped
                    {
                        _dialogService.ShowError("Redownload Queue Failed", sb.ToString().Trim());
                    }
                    else // All succeeded or skipped (already queued)
                    {
                        _dialogService.ShowInformation("Redownload Queued", sb.ToString().Trim());
                    }


                    if (addedCount > 0)
                    {
                        _navigationService.RequestTabSwitch("Downloader"); // Navigate if items were added
                    }
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteRedownloadModsAsync] Operation cancelled.");
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Queueing mods for redownload was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecuteRedownloadModsAsync] Error: {ex}");
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                RunOnUIThread(() => _dialogService.ShowError("Redownload Error", $"An unexpected error occurred: {ex.Message}"));
            }
            finally
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false);
            }
        }


        private bool CanExecutizeMod(ModItem mod)
        {
            return !IsParentLoading && mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion;
        }

        private async Task ExecuteCustomizeMod(ModItem mod)
        {
            if (mod == null) return;

            ModCustomInfo customInfo = null;
            CustomizeModDialogViewModel viewModel = null;
            ModCustomizationResult result = ModCustomizationResult.Cancel; // Default result

            // Indicate loading potentially (optional, if it's quick, maybe not needed)
            // IsLoadingRequest?.Invoke(this, true); // Consider if needed

            try
            {
                Debug.WriteLine($"Attempting to customize mod: {mod.PackageId}");

                // 1. Load data in the background (Keep this)
                customInfo = await Task.Run(() => _modService.GetCustomModInfo(mod.PackageId));
                // Execution resumes here, potentially on UI thread or background thread after await

                // 2. Ensure subsequent UI operations run on the UI thread.
                // Use the RunOnUIThread helper from ViewModelBase.
                RunOnUIThread(() =>
                {
                    // Create ViewModel (safe on UI thread)
                    viewModel = new CustomizeModDialogViewModel(mod, customInfo, _modService);

                    Debug.WriteLine("Showing customize dialog on UI thread...");

                    // Show Dialog (Must be on UI thread)
                    result = _dialogService.ShowCustomizeModDialog(viewModel); // ShowDialog blocks here

                    Debug.WriteLine($"Dialog result: {result}");

                    if (result == ModCustomizationResult.Save)
                    {
                        Debug.WriteLine("Requesting data refresh after save...");
                        // Trigger refresh event (also safer on UI thread)
                        RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteCustomizeMod: {ex}");
                // Show error message on UI thread
                RunOnUIThread(() => _dialogService.ShowError("Customization Error", $"Failed to customize mod '{mod?.Name ?? "Unknown"}': {ex.Message}"));
            }
            finally
            {
                // IsLoadingRequest?.Invoke(this, false); // Turn off loading indicator if used
            }
        }


        public void RefreshPathValidity()
        {
            var gamePath = _pathService.GetGamePath();
            var modsPath = _pathService.GetModsPath();
            var configPath = _pathService.GetConfigPath();

            HasValidPaths = !string.IsNullOrEmpty(gamePath) &&
                           !string.IsNullOrEmpty(modsPath) &&
                           !string.IsNullOrEmpty(configPath);
        }


    }
}
