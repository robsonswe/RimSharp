#nullable enable
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
using System.Globalization;
using System.Collections.Concurrent;
using RimSharp.Features.ModManager.Dialogs.Strip; // For CultureInfo
using System.Xml.Linq;

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
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor;
        // State properties (Remain here)
        private bool _isParentLoading;
        private bool _hasUnsavedChanges;
        // FIX: Changed to a nullable type to represent the state where nothing is selected.
        private ModItem? _selectedMod; // For single-item actions
        // FIX: Changed to a nullable type to represent the state where nothing is selected.
        private IList? _selectedItems; // For multi-item actions
        protected bool CanExecuteSimpleCommands() => !IsParentLoading && HasValidPaths;
        private const int MaxParallelRedownloadOperations = 10;


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

        // FIX: Property is now nullable to match its backing field.
        public ModItem? SelectedMod
        {
            get => _selectedMod;
            set
            {
                // Use base SetProperty, command observation handles updates
                SetProperty(ref _selectedMod, value);
                // Manual RaiseCanExecuteChangedForAllCommands() removed
            }
        }

        // FIX: Property is now nullable to match its backing field.
        public IList? SelectedItems // Bound from ListBox typically
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
        // FIX: Initialized with null-forgiving operator (!) because the compiler can't verify
        // that the InitializeCommands() method (called from ctor) assigns them.
        // List Management
        public ICommand ClearActiveListCommand { get; private set; } = null!;
        public ICommand SortActiveListCommand { get; private set; } = null!;
        public ICommand SaveCommand { get; private set; } = null!;
        public ICommand ImportListCommand { get; private set; } = null!;
        public ICommand ExportListCommand { get; private set; } = null!;
        public ICommand CheckReplacementsCommand { get; private set; } = null!;

        // Mod Actions (Single/Multi)
        public ICommand DeleteModCommand { get; private set; } = null!; // Single
        public ICommand DeleteModsCommand { get; private set; } = null!; // Multi
        public ICommand OpenModFoldersCommand { get; private set; } = null!; // Multi
        public ICommand OpenUrlsCommand { get; private set; } = null!; // Multi
        public ICommand OpenWorkshopPagesCommand { get; private set; } = null!; // Multi
        public ICommand OpenOtherUrlsCommand { get; private set; } = null!; // Multi

        // Tools & Analysis
        public ICommand ResolveDependenciesCommand { get; private set; } = null!;
        public ICommand CheckIncompatibilitiesCommand { get; private set; } = null!;
        public ICommand CheckDuplicatesCommand { get; private set; } = null!;

        // Placeholders
        public ICommand StripModsCommand { get; private set; } = null!;
        public ICommand FixIntegrityCommand { get; private set; } = null!;
        public ICommand RunGameCommand { get; private set; } = null!;
        public ICommand CustomizeModCommand { get; private set; } = null!;

        // Installation
        public ICommand InstallFromZipCommand { get; private set; } = null!;
        public ICommand InstallFromGithubCommand { get; private set; } = null!;
        public ICommand RedownloadModsCommand { get; private set; } = null!;

        // Events (Remain here)
        // FIX: Initialized with null-forgiving operator. Events should be handled carefully.
        // If no subscribers are expected, they can be left as is, but this silences the warning.
        public event EventHandler<bool> IsLoadingRequest = null!;
        public event EventHandler RequestDataRefresh = null!;
        public event EventHandler<bool> HasUnsavedChangesRequest = null!;

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
            IApplicationNavigationService navigationService,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor)
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
            _steamWorkshopQueueProcessor = steamWorkshopQueueProcessor;
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
        private bool CanExecuteRedownloadMods(IList? selectedItems)
        {
            // FIX: The parameter is now correctly nullable.
            var currentSelection = selectedItems ?? this.SelectedItems;

            bool isLoading = IsParentLoading;
            int count = currentSelection?.Count ?? 0;
            bool hasSelection = currentSelection != null && count > 0;

            Debug.WriteLine($"[CanExecuteRedownloadMods] Check: IsLoading={isLoading}, VM.SelectedItems.Count={count}");

            if (isLoading || !hasSelection)
            {
                Debug.WriteLine($"[CanExecuteRedownloadMods] Result: false (Loading or No Selection)");
                return false;
            }

            // FIX: Safely cast after verifying the list is not null to resolve CS8604.
            if (currentSelection is null)
            {
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

            var modsToProcess = currentSelection?.Cast<ModItem>()
                .Where(mod => mod != null && mod.ModType == ModType.WorkshopL && !string.IsNullOrEmpty(mod.SteamId) && long.TryParse(mod.SteamId, out _))
                .Select(mod => mod.SteamId!) // Select only the valid Steam IDs
                .ToList();

            if (modsToProcess == null || !modsToProcess.Any())
            {
                Debug.WriteLine("[ExecuteRedownloadModsAsync] No valid WorkshopL mods found in the selection after filtering.");
                _dialogService.ShowInformation("Redownload Mod", "No selected mods are eligible for redownload (must be locally installed Workshop mods).");
                return;
            }

            // Confirmation Dialog (using modsToProcess.Count)
            var originalMods = currentSelection!.Cast<ModItem>().Where(m => modsToProcess.Contains(m.SteamId!)).ToList(); // Get original items for name
            string confirmMessage = originalMods.Count == 1
                ? $"Are you sure you want to queue '{originalMods.First().Name}' for redownload?\n\nThis will add the mod to the download queue. It will *not* check if an update is available."
                : $"Are you sure you want to queue {originalMods.Count} Workshop mod(s) for redownload?\n\nThis will add the selected mods to the download queue. It will *not* check if updates are available.";

            var confirmResult = _dialogService.ShowConfirmation("Confirm Redownload", confirmMessage, showCancel: true);
            if (confirmResult != MessageDialogResult.OK && confirmResult != MessageDialogResult.Yes)
            {
                Debug.WriteLine("[ExecuteRedownloadModsAsync] User cancelled redownload confirmation.");
                return;
            }

            // ---- NEW LOGIC USING THE SERVICE ----
            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null;
            CancellationTokenSource? linkedCts = null;
            QueueProcessResult queueResult = new QueueProcessResult();

            try
            {
                // Setup progress dialog and cancellation
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                    "Queueing Mods for Redownload",
                    "Starting...",
                    canCancel: true,
                    isIndeterminate: false,
                    cts: null, // Will create linked CTS below
                    closeable: true);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                // Setup Progress Reporter
                var progressReporter = new Progress<QueueProcessProgress>(update =>
                {
                    // Run progress updates on UI thread
                    RunOnUIThread(() =>
                    {
                        if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                        {
                            progressDialog.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                            progressDialog.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                        }
                    });
                });

                // Call the central service
                queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(modsToProcess, progressReporter, combinedToken);

                // --- Show Summary ---
                await RunOnUIThreadAsync(() =>
                {
                    if (queueResult.WasCancelled)
                    {
                        Debug.WriteLine("Redownload operation cancelled.", nameof(ModActionsViewModel));
                        progressDialog?.ForceClose();
                        _dialogService.ShowWarning("Operation Cancelled", "Queueing mods for redownload was cancelled.");
                        return;
                    }

                    progressDialog?.CompleteOperation("Redownload queueing complete.");

                    var sb = new StringBuilder();
                    if (queueResult.SuccessfullyAdded > 0) sb.AppendLine($"{queueResult.SuccessfullyAdded} mod(s) added to the download queue.");
                    else sb.AppendLine("No new mods were added to the download queue.");

                    if (queueResult.AlreadyQueued > 0) sb.AppendLine($"{queueResult.AlreadyQueued} selected mod(s) were already in the queue.");
                    if (queueResult.FailedProcessing > 0)
                    {
                        sb.AppendLine($"{queueResult.FailedProcessing} selected mod(s) could not be added due to errors:");
                        foreach (var errMsg in queueResult.ErrorMessages.Take(5)) // Show first few errors
                        {
                            sb.AppendLine($"  - {errMsg}");
                        }
                        if (queueResult.ErrorMessages.Count > 5) sb.AppendLine("    (Check logs for more details...)");
                    }

                    // Show appropriate dialog based on result counts
                    if (queueResult.FailedProcessing > 0 && queueResult.SuccessfullyAdded > 0) // Partial success
                        _dialogService.ShowWarning("Redownload Partially Queued", sb.ToString().Trim());
                    else if (queueResult.FailedProcessing > 0 && queueResult.SuccessfullyAdded == 0) // All failed or skipped
                        _dialogService.ShowError("Redownload Queue Failed", sb.ToString().Trim());
                    else // All succeeded or skipped (already queued)
                        _dialogService.ShowInformation("Redownload Queued", sb.ToString().Trim());

                    // Navigate if items were added
                    if (queueResult.SuccessfullyAdded > 0)
                    {
                        _navigationService.RequestTabSwitch("Downloader");
                    }
                });
            }
            catch (OperationCanceledException) // Catch cancellation from WaitAsync or linked CTS propagation
            {
                // Logged and handled by the UI thread check above if queueResult.WasCancelled is true
                Debug.WriteLine("[ExecuteRedownloadModsAsync] Operation cancelled (caught top level).", nameof(ModActionsViewModel));
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
            }
            catch (Exception ex) // Catch unexpected errors
            {
                //_logger.LogException(ex, "[ExecuteRedownloadModsAsync] Outer error", nameof(ModActionsViewModel));
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                RunOnUIThread(() => _dialogService.ShowError("Redownload Error", $"An unexpected error occurred: {ex.Message}"));
            }
            finally
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose()); // Ensure closed
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false);
            }
            // ---- END OF NEW LOGIC ----
        }


        private bool CanExecutizeMod(ModItem? mod)
        {
            // FIX: Parameter is now nullable.
            return !IsParentLoading && mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion;
        }

        private async Task ExecuteCustomizeMod(ModItem mod)
        {
            if (mod == null) return;

            // FIX: Declared as nullable to correctly handle the case where it might be null.
            ModCustomInfo? customInfo = null;
            CustomizeModDialogViewModel? viewModel = null;
            ModCustomizationResult result = ModCustomizationResult.Cancel; // Default result

            // Indicate loading potentially (optional, if it's quick, maybe not needed)
            // IsLoadingRequest?.Invoke(this, true); // Consider if needed

            try
            {
                Debug.WriteLine($"Attempting to customize mod: {mod.PackageId}");

                // 1. Load data in the background (Keep this)
                // FIX: GetCustomModInfo can return null, so the result is stored in a nullable variable.
                customInfo = await Task.Run(() => _modService.GetCustomModInfo(mod.PackageId));
                // Execution resumes here, potentially on UI thread or background thread after await

                // 2. Ensure subsequent UI operations run on the UI thread.
                // Use the RunOnUIThread helper from ViewModelBase.
                RunOnUIThread(() =>
                {
                    // Create ViewModel (safe on UI thread)
                    // The customInfo variable is correctly passed as a nullable type.
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
        private async Task ExecuteStripModsAsync(CancellationToken ct)
        {
            var workshopMods = _modListManager.GetAllMods().Where(m => m.ModType == ModType.WorkshopL).ToList();
            if (!workshopMods.Any())
            {
                _dialogService.ShowInformation("Strip Mods", "No locally installed Workshop mods found to strip.");
                return;
            }

            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null;
            CancellationTokenSource? linkedCts = null;
            var strippableModsVms = new List<StrippableModViewModel>();

            try
            {
                // --- Scanning Phase ---
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog("Scanning Mods", "Starting scan...", true, false);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog could not be created.");
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                string majorVersion = _pathService.GetMajorGameVersion();
                var processedCount = 0;

                await Task.Run(async () =>
                {
                    foreach (var mod in workshopMods)
                    {
                        combinedToken.ThrowIfCancellationRequested();
                        processedCount++;
                        progressDialog.UpdateProgress((int)((double)processedCount / workshopMods.Count * 100), $"Scanning: {mod.Name}");

                        var strippableModVm = new StrippableModViewModel(mod);
                        await ScanDirectoryForStrippableItems(new DirectoryInfo(mod.Path), mod.Path, strippableModVm, majorVersion, combinedToken);

                        if (strippableModVm.Children.Any())
                        {
                            strippableModsVms.Add(strippableModVm);
                        }
                    }
                }, combinedToken);

                await RunOnUIThreadAsync(() => progressDialog.ForceClose());

                // --- Dialog Phase ---
                if (!strippableModsVms.Any())
                {
                    _dialogService.ShowInformation("Strip Mods", "Scan complete. No unnecessary files or folders found.");
                    return;
                }

                (bool shouldStrip, IEnumerable<string>? pathsToDelete) = (false, null);
                await RunOnUIThreadAsync(() =>
                {
                    // <<< UPDATED: Now instantiates the external ViewModel >>>
                    var dialogViewModel = new StripDialogViewModel(strippableModsVms);
                    (shouldStrip, pathsToDelete) = _dialogService.ShowStripModsDialog(dialogViewModel);
                });

                if (!shouldStrip || pathsToDelete == null || !pathsToDelete.Any())
                {
                    _dialogService.ShowInformation("Strip Mods", "Operation cancelled by user.");
                    return;
                }

                // --- Deletion Phase ---
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog("Stripping Mods", "Deleting selected items...", false, false);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog could not be created.");

                var totalToDelete = pathsToDelete.Count();
                var deletedCount = 0;
                long bytesFreed = 0;

                await Task.Run(() =>
                {
                    foreach (var path in pathsToDelete)
                    {
                        try
                        {
                            if (File.Exists(path))
                            {
                                var fileInfo = new FileInfo(path);
                                bytesFreed += fileInfo.Length;
                                File.Delete(path);
                            }
                            else if (Directory.Exists(path))
                            {
                                var dirInfo = new DirectoryInfo(path);
                                bytesFreed += dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                                Directory.Delete(path, true);
                            }
                            deletedCount++;
                            progressDialog.UpdateProgress((int)((double)deletedCount / totalToDelete * 100), $"Deleting: {Path.GetFileName(path)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to delete {path}: {ex.Message}");
                        }
                    }
                });

                await RunOnUIThreadAsync(() => progressDialog.ForceClose());
                _dialogService.ShowInformation("Strip Complete", $"Successfully stripped {deletedCount} items, freeing approximately {(bytesFreed / 1024.0 / 1024.0):F2} MB.");
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                _dialogService.ShowWarning("Operation Cancelled", "The strip mods operation was cancelled.");
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                _dialogService.ShowError("Strip Error", $"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        private async Task ScanDirectoryForStrippableItems(DirectoryInfo directory, string modRootPath, StrippableModViewModel strippableModVm, string majorGameVersion, CancellationToken ct)
        {
            var strippableNameComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Source", ".github", ".vs", ".vscode" };
            char[] nameDelimiters = { '-', '_', ' ' };

            // This dictionary will hold all items we *think* should be deleted.
            var potentialDeletions = new Dictionary<string, (string Name, string RelativePath, string FullPath, long Size, StrippableItemType Type)>(StringComparer.OrdinalIgnoreCase);

            // --- Phase 1: Identify and flag old version folders ---
            var versionLikeFolders = new List<(DirectoryInfo Dir, Version Ver)>();
            foreach (var dir in directory.EnumerateDirectories())
            {
                var versionString = new string(dir.Name.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');
                if (!string.IsNullOrEmpty(versionString) && Version.TryParse(versionString, out var ver))
                {
                    versionLikeFolders.Add((dir, ver));
                }
            }

            if (versionLikeFolders.Any())
            {
                var latestVersion = versionLikeFolders.Max(v => v.Ver);
                foreach (var (dir, ver) in versionLikeFolders)
                {
                    bool keep = dir.Name.Contains(majorGameVersion) || ver.Equals(latestVersion);
                    if (!keep)
                    {
                        long size = await Task.Run(() => dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length), ct);
                        potentialDeletions[dir.FullName] = (dir.Name, Path.GetRelativePath(modRootPath, dir.FullName), dir.FullName, size, StrippableItemType.Folder);
                    }
                }
            }

            // --- Phase 2: Recursively find junk files/folders everywhere ---
            async Task FindJunkRecursively(DirectoryInfo currentDir)
            {
                ct.ThrowIfCancellationRequested();

                // If this directory is already marked for deletion, no need to scan inside it.
                if (potentialDeletions.ContainsKey(currentDir.FullName)) return;

                // Scan files for junk
                foreach (var file in currentDir.EnumerateFiles().Where(f => f.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase)))
                {
                    potentialDeletions[file.FullName] = (file.Name, Path.GetRelativePath(modRootPath, file.FullName), file.FullName, file.Length, StrippableItemType.File);
                }

                // Scan subdirectories for junk
                foreach (var subDir in currentDir.EnumerateDirectories())
                {
                    bool isJunkByName = subDir.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                                        subDir.Name.Split(nameDelimiters).Any(part => strippableNameComponents.Contains(part));

                    if (isJunkByName)
                    {
                        long size = await Task.Run(() => subDir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length), ct);
                        potentialDeletions[subDir.FullName] = (subDir.Name, Path.GetRelativePath(modRootPath, subDir.FullName), subDir.FullName, size, StrippableItemType.Folder);
                    }
                    else
                    {
                        // Not junk by name, so scan inside it.
                        await FindJunkRecursively(subDir);
                    }
                }
            }
            await FindJunkRecursively(directory);

            // --- Phase 3: Use loadFolders.xml to "rescue" any items that were incorrectly flagged ---
            var loadFoldersFile = directory.GetFiles("loadFolders.xml", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (loadFoldersFile != null && potentialDeletions.Any())
            {
                try
                {
                    var doc = XDocument.Load(loadFoldersFile.FullName);
                    var versionNode = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("v" + majorGameVersion, StringComparison.OrdinalIgnoreCase));
                    if (versionNode != null)
                    {
                        var essentialPaths = versionNode.Elements("li")
                            .Select(li => Path.GetFullPath(Path.Combine(modRootPath, li.Value.Trim().Replace('/', Path.DirectorySeparatorChar))))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var pathsToRescue = potentialDeletions.Keys
                            .Where(path => essentialPaths.Any(essential => path.Equals(essential) || path.StartsWith(essential + Path.DirectorySeparatorChar)))
                            .ToList();

                        foreach (var path in pathsToRescue)
                        {
                            potentialDeletions.Remove(path);
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Error parsing loadFolders.xml for mod {strippableModVm.Mod.Name}: {ex.Message}"); }
            }

            // --- Phase 4: Populate the final ViewModel with what's left ---
            foreach (var item in potentialDeletions.Values)
            {
                var vm = new StrippableItemViewModel(strippableModVm, item.Name, item.RelativePath, item.FullPath, item.Size, item.Type);
                await RunOnUIThreadAsync(() => strippableModVm.Children.Add(vm));
            }

            if (strippableModVm.Children.Any())
            {
                await RunOnUIThreadAsync(() => strippableModVm.UpdateParentSelectionState());
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