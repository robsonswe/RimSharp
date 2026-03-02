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
using System.Windows.Input;
using RimSharp.Features.ModManager.Dialogs.CustomizeMod;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Globalization;
using System.Collections.Concurrent;
using RimSharp.Features.ModManager.Dialogs.Strip;
using System.Xml.Linq;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel : ViewModelBase
    {
        private readonly IModDataService _dataService;
        private readonly IModCommandService _commandService;
        private readonly IModListIOService _ioService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IModIncompatibilityService _incompatibilityService;
        private readonly IModDuplicateService _duplicateService;
        private readonly IPathService _pathService;
        private readonly IModDeletionService _deletionService;

        private readonly IModService _modService;

        private readonly IModReplacementService _replacementService;
        private readonly IDownloadQueueService _downloadQueueService;
        private readonly ISteamApiClient _steamApiClient;
        private readonly IApplicationNavigationService _navigationService;
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor;
        private readonly IGitService _gitService;
        private bool _isParentLoading;
        private bool _hasUnsavedChanges;
        private ModItem? _selectedMod;
        private IList? _selectedItems;
        protected bool CanExecuteSimpleCommands() => !IsParentLoading && HasValidPaths;
        private const int MaxParallelRedownloadOperations = 10;

public bool IsParentLoading
        {
            get => _isParentLoading;
            set
            {
                Debug.WriteLine($"[ModActionsViewModel] IsParentLoading SETTER: Current = {_isParentLoading}, New = {value}");

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

                // if (SetProperty(ref _hasUnsavedChanges, value))
                // {

                //     // HasUnsavedChangesRequest?.Invoke(this, value);

                SetProperty(ref _hasUnsavedChanges, value);
            }
        }
        public ModItem? SelectedMod
        {
            get => _selectedMod;
            set
            {

                SetProperty(ref _selectedMod, value);
            }
        }
        public IList? SelectedItems // Bound from ListBox typically
        {
            get => _selectedItems;
            set
            {

                SetProperty(ref _selectedItems, value);
            }
        }

        private bool _hasValidPaths;
        public bool HasValidPaths
        {
            get => _hasValidPaths;
            private set => SetProperty(ref _hasValidPaths, value);
        }

        private bool _isGameRunning;
        public bool IsGameRunning
        {
            get => _isGameRunning;
            private set => SetProperty(ref _isGameRunning, value);
        }

        private bool _isLaunchingGame;
        public bool IsLaunchingGame
        {
            get => _isLaunchingGame;
            private set => SetProperty(ref _isLaunchingGame, value);
        }

        private bool _isViewActive;
        public bool IsViewActive
        {
            get => _isViewActive;
            set
            {
                if (SetProperty(ref _isViewActive, value))
                {
                    if (value) _gameCheckTimer?.Start();
                    else _gameCheckTimer?.Stop();
                }
            }
        }

        public int ActiveSortingIssuesCount => _modListManager.ActiveSortingIssuesCount;
        public int ActiveMissingDependenciesCount => _modListManager.ActiveMissingDependenciesCount;
        public int ActiveIncompatibilitiesCount => _modListManager.ActiveIncompatibilitiesCount;
        public int ActiveVersionMismatchCount => _modListManager.ActiveVersionMismatchCount;
        public int ActiveDuplicateIssuesCount => _modListManager.ActiveDuplicateIssuesCount;
        public int TotalActiveIssues => ActiveMissingDependenciesCount + 
                                       ActiveIncompatibilitiesCount + 
                                       ActiveDuplicateIssuesCount;

        private readonly Avalonia.Threading.DispatcherTimer _gameCheckTimer;

        // List Management
        public ICommand ClearActiveListCommand { get; private set; } = null!;
        public ICommand SortActiveListCommand { get; private set; } = null!;
        public ICommand SaveCommand { get; private set; } = null!;
        public ICommand ImportListCommand { get; private set; } = null!;
        public ICommand ExportListCommand { get; private set; } = null!;
        public ICommand CheckReplacementsCommand { get; private set; } = null!;

        // Mod Actions (Single/Multi)
        public ICommand DeleteModCommand { get; private set; } = null!; 
        public ICommand DeleteModsCommand { get; private set; } = null!; 
        public ICommand OpenModFoldersCommand { get; private set; } = null!; 
        public ICommand OpenUrlsCommand { get; private set; } = null!; 
        public ICommand OpenWorkshopPagesCommand { get; private set; } = null!; 
        public ICommand OpenOtherUrlsCommand { get; private set; } = null!; 
        // Tools & Analysis
        public ICommand ResolveDependenciesCommand { get; private set; } = null!;
        public ICommand CheckIncompatibilitiesCommand { get; private set; } = null!;
        public ICommand CheckDuplicatesCommand { get; private set; } = null!;

        public ICommand StripModsCommand { get; private set; } = null!;
        public ICommand FixIntegrityCommand { get; private set; } = null!;
        public ICommand RunGameCommand { get; private set; } = null!;
        public ICommand CustomizeModCommand { get; private set; } = null!;

        // Installation
        public ICommand InstallFromZipCommand { get; private set; } = null!;
        public ICommand InstallFromGithubCommand { get; private set; } = null!;
        public ICommand RedownloadModsCommand { get; private set; } = null!;
        public event EventHandler<bool>? IsLoadingRequest;
        public event EventHandler? RequestDataRefresh;
        public event EventHandler<bool>? HasUnsavedChangesRequest;

        private bool _installSuccess = false;

        public ModActionsViewModel(
            IModDataService dataService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IModDuplicateService duplicateService,
            IModDeletionService deletionService,
            IDialogService dialogService,
            IPathService pathService,
            IModService modService,
            IModReplacementService replacementService,
            IDownloadQueueService downloadQueueService,
            ISteamApiClient steamApiClient,
            IApplicationNavigationService navigationService,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor,
            IGitService gitService)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _duplicateService = duplicateService;
            _deletionService = deletionService;
            _dialogService = dialogService;
            _pathService = pathService;
            _modService = modService;
            _replacementService = replacementService;
            _pathService.RefreshPaths();
            _downloadQueueService = downloadQueueService;
            _steamApiClient = steamApiClient;
            _navigationService = navigationService;
            _steamWorkshopQueueProcessor = steamWorkshopQueueProcessor;
            _gitService = gitService;
            RefreshPathValidity();

            _modListManager.ListChanged += OnModListChanged;

            _gameCheckTimer = new Avalonia.Threading.DispatcherTimer();
            _gameCheckTimer.Interval = TimeSpan.FromSeconds(2);
            _gameCheckTimer.Tick += (s, e) => CheckIfGameIsRunning();
            
            InitializeCommands(); // Calls partial initialization methods
        }

        private void OnModListChanged(object? sender, ModListChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ActiveSortingIssuesCount));
            OnPropertyChanged(nameof(ActiveMissingDependenciesCount));
            OnPropertyChanged(nameof(ActiveIncompatibilitiesCount));
            OnPropertyChanged(nameof(ActiveVersionMismatchCount));
            OnPropertyChanged(nameof(ActiveDuplicateIssuesCount));
            OnPropertyChanged(nameof(TotalActiveIssues));
        }

        private void CheckIfGameIsRunning()
        {
            try
            {

                var p64 = Process.GetProcessesByName("RimWorldWin64");
                var p32 = Process.GetProcessesByName("RimWorld");

                bool isRunning = p64.Length > 0 || p32.Length > 0;

                foreach (var p in p64) p.Dispose();
                foreach (var p in p32) p.Dispose();

                IsGameRunning = isRunning;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModActionsViewModel] Error checking game status: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _gameCheckTimer?.Stop();
                    _modListManager.ListChanged -= OnModListChanged;
                }
            }
            base.Dispose(disposing);
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

                    var currentSelection = SelectedItems;
                    return CanExecuteRedownloadMods(currentSelection);
                },
                // Observation still works correctly on the ViewModel's properties
                observedProperties: new[] { nameof(SelectedItems), nameof(IsParentLoading) });
        }
        private bool CanExecuteRedownloadMods(IList? selectedItems)
        {
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
            if (currentSelection is null)
            {
                return false;
            }
            bool anyValid = false;
            try
            {
                anyValid = currentSelection.Cast<ModItem>().ToList().Any(mod =>
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

            Debug.WriteLine($"[CanExecuteRedownloadMods] AnyValid Check Result: {anyValid}. Final CanExecute: {anyValid}");
            return anyValid; 
        }

        private async Task ExecuteRedownloadModsAsync(IList? selectedItems, CancellationToken ct)
        {
            var currentSelection = selectedItems ?? SelectedItems;

            var modsToProcess = currentSelection?.Cast<ModItem>()
                .Where(mod => mod != null && mod.ModType == ModType.WorkshopL && !string.IsNullOrEmpty(mod.SteamId) && long.TryParse(mod.SteamId, out _))
                .Select(mod => mod.SteamId!) // Select only the valid Steam IDs
                .ToList();

            if (modsToProcess == null || !modsToProcess.Any())
            {
                Debug.WriteLine("[ExecuteRedownloadModsAsync] No valid WorkshopL mods found in the selection after filtering.");
                await _dialogService.ShowInformation("Redownload Mod", "No selected mods are eligible for redownload (must be locally installed Workshop mods).");
                return;
            }

            // Confirmation Dialog (using modsToProcess.Count)
            var originalMods = currentSelection!.Cast<ModItem>().Where(m => modsToProcess.Contains(m.SteamId!)).ToList(); // Get original items for name
            string confirmMessage = originalMods.Count == 1
                ? $"Are you sure you want to queue '{originalMods.First().Name}' for redownload?\n\nThis will add the mod to the download queue. It will *not* check if an update is available."
                : $"Are you sure you want to queue {originalMods.Count} Workshop mod(s) for redownload?\n\nThis will add the selected mods to the download queue. It will *not* check if updates are available.";

            var confirmResult = await _dialogService.ShowConfirmationAsync("Confirm Redownload", confirmMessage, showCancel: true);
            if (confirmResult != MessageDialogResult.OK && confirmResult != MessageDialogResult.Yes)
            {
                Debug.WriteLine("[ExecuteRedownloadModsAsync] User cancelled redownload confirmation.");
                return;
            }
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
                    isIndeterminate: false, // Explicitly set to false as we update progress
                    cts: null, // Will create linked CTS below
                    closeable: false);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                // Setup Progress Reporter
                var progressReporter = new Progress<QueueProcessProgress>(update =>
                {
                    RunOnUIThread(() =>
                    {
                        if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                        {
                            progressDialog.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                            progressDialog.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                        }
                    });
                });
                queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(modsToProcess, progressReporter, combinedToken);
                await RunOnUIThreadAsync(async () =>
                {
                    if (queueResult.WasCancelled)
                    {
                        Debug.WriteLine("Redownload operation cancelled.", nameof(ModActionsViewModel));
                        progressDialog?.ForceClose();
                        await _dialogService.ShowWarning("Operation Cancelled", "Queueing mods for redownload was cancelled.");
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
                        await _dialogService.ShowWarning("Redownload Partially Queued", sb.ToString().Trim());
                    else if (queueResult.FailedProcessing > 0 && queueResult.SuccessfullyAdded == 0) // All failed or skipped
                        await _dialogService.ShowError("Redownload Queue Failed", sb.ToString().Trim());
                    else // All succeeded or skipped (already queued)
                        await _dialogService.ShowInformation("Redownload Queued", sb.ToString().Trim());
                    if (queueResult.SuccessfullyAdded > 0)
                    {
                        _navigationService.RequestTabSwitch("Downloader");
                    }
                });
            }
            catch (OperationCanceledException) // Catch cancellation from WaitAsync or linked CTS propagation
            {

                Debug.WriteLine("[ExecuteRedownloadModsAsync] Operation cancelled (caught top level).", nameof(ModActionsViewModel));
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
            }
            catch (Exception ex) // Catch unexpected errors
            {
                //_logger.LogException(ex, "[ExecuteRedownloadModsAsync] Outer error", nameof(ModActionsViewModel));
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Redownload Error", $"An unexpected error occurred: {ex.Message}"));
            }
            finally
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false);
            }
        }

private bool CanExecutizeMod(ModItem? mod)
        {
            return !IsParentLoading && mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion;
        }

        private async Task ExecuteCustomizeMod(ModItem mod)
        {
            if (mod == null) return;

            ModCustomInfo? customInfo = null;
            CustomizeModDialogViewModel? viewModel = null;
            ModCustomizationResult result = ModCustomizationResult.Cancel;

            try
            {
                Debug.WriteLine($"Attempting to customize mod: {mod.PackageId}");
                customInfo = await Task.Run(() => _modService.GetCustomModInfo(mod.PackageId));

                // Create ViewModel and show dialog on UI thread
                viewModel = new CustomizeModDialogViewModel(mod, customInfo ?? new ModCustomInfo(), _modService, _dialogService);

                Debug.WriteLine("Showing customize dialog on UI thread...");

                // Show Dialog (blocks until dialog closes)
                result = await _dialogService.ShowCustomizeModDialog(viewModel);

                Debug.WriteLine($"Dialog result: {result}");

                if (result == ModCustomizationResult.Save)
                {
                    Debug.WriteLine("Requesting data refresh after save...");
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteCustomizeMod: {ex}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Customization Error", $"Failed to customize mod '{mod?.Name ?? "Unknown"}': {ex.Message}"));
            }
        }

        private async Task ExecuteStripModsAsync(CancellationToken ct)
        {
            var workshopMods = _modListManager.GetAllMods().Where(m => m.ModType == ModType.WorkshopL).ToList();
            if (!workshopMods.Any())
            {
                await _dialogService.ShowInformation("Strip Mods", "No locally installed Workshop mods found to strip.");
                return;
            }

            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null;
            CancellationTokenSource? linkedCts = null;
            var strippableModsVms = new List<StrippableModViewModel>();

            try
            {
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
                        await ScanDirectoryRecursively(
                            new DirectoryInfo(mod.Path),
                            mod.Path,
                            strippableModVm,
                            majorVersion,
                            isModRoot: true,
                            ct: combinedToken
                        );

                        if (strippableModVm.Children.Any())
                        {
                            strippableModsVms.Add(strippableModVm);
                        }
                    }
                }, combinedToken);

                await RunOnUIThreadAsync(() => progressDialog.ForceClose());
                if (!strippableModsVms.Any())
                {
                    await _dialogService.ShowInformation("Strip Mods", "Scan complete. No unnecessary files or folders found.");
                    return;
                }

                (bool shouldStrip, IEnumerable<string>? pathsToDelete) = (false, null);
                await RunOnUIThreadAsync(async () =>
                {
                    var dialogViewModel = new StripDialogViewModel(strippableModsVms);
                    (shouldStrip, pathsToDelete) = await _dialogService.ShowStripModsDialogAsync(dialogViewModel);
                });

                if (!shouldStrip || pathsToDelete == null || !pathsToDelete.Any())
                {

                    return;
                }
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
                await _dialogService.ShowInformation("Strip Complete", $"Successfully stripped {deletedCount} items, freeing approximately {(bytesFreed / 1024.0 / 1024.0):F2} MB.");
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await _dialogService.ShowWarning("Operation Cancelled", "The strip mods operation was cancelled.");
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await _dialogService.ShowError("Strip Error", $"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        private async Task ScanDirectoryRecursively(
            DirectoryInfo currentDir,
            string modRootPath,
            StrippableModViewModel strippableModVm,
            string majorGameVersion,
            bool isModRoot,
            CancellationToken ct,
            IDictionary<string, (string Name, string RelativePath, string FullPath, long Size, StrippableItemType Type)>? potentialDeletions = null)
        {
            potentialDeletions ??= new Dictionary<string, (string, string, string, long, StrippableItemType)>(StringComparer.OrdinalIgnoreCase);

            ct.ThrowIfCancellationRequested();

            if (potentialDeletions.ContainsKey(currentDir.FullName)) return;
            var junkNameComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Source", ".git", ".github", ".vs", ".vscode" };
            var exactJunkFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "Properties" };
            var junkFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csproj", ".csproj.user", ".sln" };
            char[] nameDelimiters = { '-', '_', ' ' };
            var versionFolders = new List<(string VersionString, DirectoryInfo Dir)>();
            var otherFolders = new List<DirectoryInfo>();

            foreach (var dir in currentDir.EnumerateDirectories())
            {
                
                var potentialVersionString = dir.Name.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? dir.Name.Substring(1) : dir.Name;
                var versionString = new string(potentialVersionString.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');

                if (Version.TryParse(versionString, out _))
                {
                    versionFolders.Add((versionString, dir));
                }
                else
                {
                    otherFolders.Add(dir);
                }
            }
            if (versionFolders.Any())
            {
                var essentialVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (isModRoot)
                {
                    // Full, nuanced logic for the mod's root directory.
                    var supportedVersions = strippableModVm.Mod.SupportedVersionStrings.ToHashSet();

                    var latestAvailableSupported = versionFolders
                        .Where(vf => supportedVersions.Contains(vf.VersionString))
                        .Select(vf => Version.TryParse(vf.VersionString, out var v) ? v : null)
                        .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();

                    if (latestAvailableSupported != null) essentialVersions.Add($"{latestAvailableSupported.Major}.{latestAvailableSupported.Minor}");

                    if (supportedVersions.Contains(majorGameVersion) && versionFolders.Any(vf => vf.VersionString.Equals(majorGameVersion)))
                    {
                        essentialVersions.Add(majorGameVersion);
                    }
                }
                else
                {

                    var latestAvailable = versionFolders
                        .Select(vf => Version.TryParse(vf.VersionString, out var v) ? v : null)
                        .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();

                    if (latestAvailable != null) essentialVersions.Add($"{latestAvailable.Major}.{latestAvailable.Minor}");
                }

                foreach (var (versionString, dir) in versionFolders)
                {
                    if (!essentialVersions.Contains(versionString))
                    {
                        long size = await Task.Run(() => dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length), ct);
                        potentialDeletions[dir.FullName] = (dir.Name, Path.GetRelativePath(modRootPath, dir.FullName), dir.FullName, size, StrippableItemType.Folder);
                    }
                }
            }
            foreach (var otherDir in otherFolders)
            {
                bool isJunkByNameComponent = otherDir.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                                             otherDir.Name.Split(nameDelimiters).Any(part => junkNameComponents.Contains(part));
                bool isExactJunkFolder = exactJunkFolders.Contains(otherDir.Name);

                if (isJunkByNameComponent || isExactJunkFolder)
                {
                    long size = await Task.Run(() => otherDir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length), ct);
                    potentialDeletions[otherDir.FullName] = (otherDir.Name, Path.GetRelativePath(modRootPath, otherDir.FullName), otherDir.FullName, size, StrippableItemType.Folder);
                }
                else
                {

                    await ScanDirectoryRecursively(otherDir, modRootPath, strippableModVm, majorGameVersion, isModRoot: false, ct, potentialDeletions);
                }
            }
            foreach (var file in currentDir.EnumerateFiles())
            {
                bool isGitFile = file.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase);
                bool isJunkExtension = junkFileExtensions.Contains(file.Extension);

                if (isGitFile || isJunkExtension)
                {
                    potentialDeletions[file.FullName] = (file.Name, Path.GetRelativePath(modRootPath, file.FullName), file.FullName, file.Length, StrippableItemType.File);
                }
            }
            if (isModRoot)
            {
                var loadFoldersFile = new DirectoryInfo(modRootPath).GetFiles("loadFolders.xml", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (loadFoldersFile != null && potentialDeletions.Any())
                {
                    try
                    {
                        var doc = XDocument.Load(loadFoldersFile.FullName);
                        var pathsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        var rootEssentialVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var supported = strippableModVm.Mod.SupportedVersionStrings.ToHashSet();
                        if (supported.Contains(majorGameVersion)) rootEssentialVersions.Add(majorGameVersion);

                        var latestSupported = strippableModVm.Mod.SupportedVersionStrings
                            .Select(vStr => Version.TryParse(vStr, out var v) ? v : null)
                            .Where(v => v != null).OrderByDescending(v => v).FirstOrDefault();
                        if (latestSupported != null) rootEssentialVersions.Add($"{latestSupported.Major}.{latestSupported.Minor}");

                        foreach (var version in rootEssentialVersions)
                        {
                            var versionNode = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("v" + version, StringComparison.OrdinalIgnoreCase));
                            if (versionNode != null)
                            {
                                var pathsForThisVersion = versionNode.Elements("li")
                                    .Select(li => Path.GetFullPath(Path.Combine(modRootPath, li.Value.Trim().Replace('/', Path.DirectorySeparatorChar))));
                                foreach (var path in pathsForThisVersion) { pathsToKeep.Add(path); }
                            }
                        }

                        var pathsToRescue = potentialDeletions.Keys
                            .Where(path => pathsToKeep.Any(keepPath => path.Equals(keepPath, StringComparison.OrdinalIgnoreCase) || path.StartsWith(keepPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                            .ToList();

                        foreach (var path in pathsToRescue)
                        {
                            potentialDeletions.Remove(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error parsing loadFolders.xml for mod {strippableModVm.Mod.Name}: {ex.Message}");
                    }
                }

                foreach (var item in potentialDeletions.Values.OrderBy(i => i.RelativePath))
                {
                    var vm = new StrippableItemViewModel(strippableModVm, item.Name, item.RelativePath, item.FullPath, item.Size, item.Type);
                    await RunOnUIThreadAsync(() => strippableModVm.Children.Add(vm));
                }

                if (strippableModVm.Children.Any())
                {
                    await RunOnUIThreadAsync(() => strippableModVm.UpdateParentSelectionState());
                }
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


