using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
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
using System.IO.Compression;
using Microsoft.Win32;
using System.Xml.Linq;

namespace RimSharp.Features.ModManager.ViewModels
{
    public class ModActionsViewModel : ViewModelBase
    {
        // Dependencies
        private readonly IModDataService _dataService;
        private readonly IModCommandService _commandService;
        private readonly IModListIOService _ioService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IModIncompatibilityService _incompatibilityService;
        private readonly IPathService _pathService;

        // State properties (likely mirrored or controlled by parent)
        private bool _isParentLoading;
        private bool _hasUnsavedChanges;
        private ModItem _selectedMod; // For single-item actions
        private IList _selectedItems; // For multi-item actions

        public bool IsParentLoading
        {
            get => _isParentLoading;
            set
            {
                if (SetProperty(ref _isParentLoading, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (SetProperty(ref _hasUnsavedChanges, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }

        public ModItem SelectedMod
        {
            get => _selectedMod;
            set
            {
                if (SetProperty(ref _selectedMod, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }

        public IList SelectedItems // Bound from ListBox typically
        {
            get => _selectedItems;
            set
            {
                if (SetProperty(ref _selectedItems, value)) RaiseCanExecuteChangedForAllCommands();
            }
        }


        // --- Commands ---
        // List Management
        public ICommand ClearActiveListCommand { get; private set; }
        public ICommand SortActiveListCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand ImportListCommand { get; private set; }
        public ICommand ExportListCommand { get; private set; }

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

        public ICommand InstallFromZipCommand { get; private set; }
        public ICommand InstallFromGithubCommand { get; private set; }


        // Event to request parent to set global IsLoading state
        public event EventHandler<bool> IsLoadingRequest;
        // Event to request parent to refresh data
        public event EventHandler RequestDataRefresh;
        // Event to request parent to update HasUnsavedChanges
        public event EventHandler<bool> HasUnsavedChangesRequest;

        public ModActionsViewModel(
            IModDataService dataService,
            IModCommandService commandService,
            IModListIOService ioService,
            IModListManager modListManager,
            IModIncompatibilityService incompatibilityService,
            IDialogService dialogService,
            IPathService pathService)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;
            _pathService = pathService;
            InitializeCommands();
        }

        private void InitializeCommands()
        {
            // Use AsyncRelayCommand from RimSharp.Core.Commands
            ClearActiveListCommand = new AsyncRelayCommand(ExecuteClearActiveList, CanExecuteSimpleCommands);
            SortActiveListCommand = new AsyncRelayCommand(ExecuteSortActiveList, CanExecuteSimpleCommands);
            ImportListCommand = new AsyncRelayCommand(ExecuteImport, CanExecuteSimpleCommands);
            ExportListCommand = new AsyncRelayCommand(ExecuteExport, CanExecuteExport);
            ResolveDependenciesCommand = new AsyncRelayCommand(ExecuteResolveDependencies, CanExecuteSimpleCommands);
            CheckIncompatibilitiesCommand = new AsyncRelayCommand(ExecuteCheckIncompatibilities, CanExecuteCheckIncompatibilities);
            InstallFromZipCommand = new AsyncRelayCommand(ExecuteInstallFromZip, CanExecuteSimpleCommands);

            // Use AsyncRelayCommand<T> from RimSharp.Core.Commands
            // Ensure the Execute methods accept (T parameter, CancellationToken ct)
            // Ensure the CanExecute methods accept (T parameter)
            DeleteModCommand = new AsyncRelayCommand<ModItem>(ExecuteDeleteModAsync, CanExecuteDeleteMod);
            DeleteModsCommand = new AsyncRelayCommand<IList>(ExecuteDeleteModsAsync, CanExecuteDeleteMods);

            // Standard RelayCommands remain the same
            SaveCommand = new RelayCommand(ExecuteSaveMods, CanExecuteSaveMods);
            CheckDuplicatesCommand = new RelayCommand(ExecuteCheckDuplicates, CanExecuteSimpleCommands);
            OpenModFoldersCommand = new RelayCommand<IList>(OpenModFolders, CanExecuteMultiSelectActions);
            OpenUrlsCommand = new RelayCommand<IList>(OpenUrls, CanExecuteMultiSelectActions);
            OpenWorkshopPagesCommand = new RelayCommand<IList>(OpenWorkshopPages, CanExecuteMultiSelectActions);
            OpenOtherUrlsCommand = new RelayCommand<IList>(OpenOtherUrls, CanExecuteMultiSelectActions);

            // Placeholders / Not Implemented
            InstallFromGithubCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "GitHub installation is not yet implemented."), _ => !IsParentLoading);
            StripModsCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Strip mods: Functionality not yet implemented."), _ => !IsParentLoading);
            FixIntegrityCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."), _ => !IsParentLoading);
            RunGameCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Run game: Functionality not yet implemented."), _ => !IsParentLoading);
        }


        private void RaiseCanExecuteChangedForAllCommands()
        {
            // Use the specific command type for correct RaiseCanExecuteChanged invocation if needed
            (ClearActiveListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SortActiveListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImportListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportListCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ResolveDependenciesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CheckIncompatibilitiesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CheckDuplicatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteModCommand as AsyncRelayCommand<ModItem>)?.RaiseCanExecuteChanged();
            (DeleteModsCommand as AsyncRelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenModFoldersCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenUrlsCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenWorkshopPagesCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (OpenOtherUrlsCommand as RelayCommand<IList>)?.RaiseCanExecuteChanged();
            (StripModsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FixIntegrityCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RunGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallFromZipCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (InstallFromGithubCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // --- CanExecute Methods ---
        // These do not change as they are synchronous predicates

        private bool CanExecuteSimpleCommands() => !IsParentLoading;
        private bool CanExecuteSaveMods() => HasUnsavedChanges && !IsParentLoading;
        private bool CanExecuteExport() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();
        private bool CanExecuteCheckIncompatibilities() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();

        private bool CanExecuteDeleteMod(ModItem mod)
        {
            mod = mod ?? SelectedMod;
            return mod != null
                && mod.ModType != ModType.Core
                && mod.ModType != ModType.Expansion
                && !string.IsNullOrEmpty(mod.Path)
                && Directory.Exists(mod.Path)
                && !IsParentLoading;
        }

        private bool CanExecuteDeleteMods(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems;
            return selectedItems != null
                && selectedItems.Count > 0
                && selectedItems.Cast<ModItem>().Any(CanBeDeleted)
                && !IsParentLoading;
        }

        private bool CanExecuteMultiSelectActions(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems;
            return selectedItems != null && selectedItems.Count > 0 && !IsParentLoading;
        }

        private bool CanBeDeleted(ModItem mod) => mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion && !string.IsNullOrEmpty(mod.Path);

        // --- Execution Methods ---
        // Methods used by AsyncRelayCommand now accept CancellationToken

        private async Task ExecuteClearActiveList(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                // Pass ct if _commandService method supports it
                await _commandService.ClearActiveModsAsync();
                HasUnsavedChangesRequest?.Invoke(this, true);
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteSortActiveList(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                // Pass ct if _commandService method supports it
                await _commandService.SortActiveModsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sorting mods: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Sort Error", $"Error sorting mods: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ExecuteSaveMods() // Stays synchronous
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeIdsToSave = _modListManager.VirtualActiveMods
                                        .Select(entry => entry.Mod.PackageId)
                                        .Where(id => !string.IsNullOrEmpty(id))
                                        .ToList();

                _dataService.SaveActiveModIdsToConfig(activeIdsToSave);
                HasUnsavedChangesRequest?.Invoke(this, false);
                Debug.WriteLine("Mod list saved successfully.");
                _dialogService.ShowInformation("Save Successful", "Current active mod list has been saved.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mods config: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Save Error", $"Failed to save mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteImport(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                // Pass ct if _ioService method supports it
                await _ioService.ImportModListAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error importing list: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Import Error", $"Failed to import mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteExport(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeModsToExport = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                if (!activeModsToExport.Any())
                {
                    _dialogService.ShowInformation("Export List", "There are no active mods to export.");
                    return;
                }
                // Pass ct if _ioService method supports it
                await _ioService.ExportModListAsync(activeModsToExport);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting list: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Export Error", $"Failed to export mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }


        private async Task ExecuteDeleteModAsync(ModItem mod, CancellationToken ct = default)
        {
            mod = mod ?? SelectedMod;
            if (!CanExecuteDeleteMod(mod))
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Precondition failed for mod '{mod?.Name}'.");
                _dialogService.ShowWarning("Deletion Blocked", $"Cannot delete mod '{mod?.Name}'. It might be Core/DLC, already deleted, or the application is busy.");
                return;
            }

            var result = _dialogService.ShowConfirmation(
               "Confirm Deletion",
               $"Are you sure you want to permanently delete the mod '{mod.Name}'?\n\nThis action cannot be undone.\n\nPath: {mod.Path}",
               showCancel: true);

            if (result != MessageDialogResult.OK) return;

            IsLoadingRequest?.Invoke(this, true);
            bool deletionSuccess = false;
            try
            {
                // Use Task.Run with cancellation token if the operation inside supports it
                // Directory.Delete itself doesn't directly support cancellation in this way
                await Task.Run(() => Directory.Delete(mod.Path, recursive: true), ct);
                deletionSuccess = true;
                _dialogService.ShowInformation("Deletion Successful", $"Mod '{mod.Name}' was deleted.");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Deletion cancelled for '{mod.Path}'.");
                _dialogService.ShowWarning("Deletion Cancelled", $"Deletion of mod '{mod.Name}' was cancelled.");
                // deletionSuccess remains false
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Error deleting '{mod.Path}': {ex}");
                _dialogService.ShowError("Deletion Error", $"Could not delete mod '{mod.Name}'.\nError: {ex.Message}");
            }
            finally
            {
                if (deletionSuccess)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    IsLoadingRequest?.Invoke(this, false);
                }
            }
        }

        private async Task ExecuteDeleteModsAsync(IList selectedItems, CancellationToken ct = default)
        {
            selectedItems = selectedItems ?? SelectedItems;
            var mods = selectedItems?.Cast<ModItem>().ToList();

            if (mods == null || !mods.Any())
            {
                _dialogService.ShowWarning("Deletion Blocked", "No mods selected for deletion.");
                return;
            }

            var deletableMods = mods.Where(CanBeDeleted).ToList();
            if (deletableMods.Count == 0)
            {
                _dialogService.ShowWarning("Deletion Blocked", "None of the selected mods can be deleted (Core/DLC or path missing).");
                return;
            }
            var nonDeletableCount = mods.Count - deletableMods.Count;

            var confirmationMessage = $"Are you sure you want to permanently delete {deletableMods.Count} mod(s)?";
            if (nonDeletableCount > 0) { /* ... add warning ... */ }
            confirmationMessage += "\n\nThis action cannot be undone.";

            var result = _dialogService.ShowConfirmation("Confirm Deletion", confirmationMessage, showCancel: true);
            if (result != MessageDialogResult.OK) return;

            var progressDialog = _dialogService.ShowProgressDialog(
               "Deleting Mods",
               $"Preparing to delete {deletableMods.Count} mods...",
               canCancel: true);

            IsLoadingRequest?.Invoke(this, true);
            var deletionResults = new List<string>();
            bool cancelled = false;
            bool anySuccess = false;

            // Create a cancellation token source linked to the dialog's cancellation
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
            var linkedCt = linkedCts.Token; // Use this token for the actual operation
            try
            {
                for (int i = 0; i < deletableMods.Count; i++)
                {
                    linkedCt.ThrowIfCancellationRequested(); // Check for cancellation before each mod

                    var mod = deletableMods[i];
                    progressDialog.Message = $"Deleting {mod.Name} ({i + 1}/{deletableMods.Count})...";
                    progressDialog.UpdateProgress((int)((double)(i + 1) / deletableMods.Count * 100.0));

                    try
                    {
                        if (Directory.Exists(mod.Path))
                        {
                             // Use Task.Run with cancellation token
                            await Task.Run(() => Directory.Delete(mod.Path, recursive: true), linkedCt);
                            deletionResults.Add($"Successfully deleted {mod.Name}");
                            anySuccess = true;
                        }
                        else
                        {
                            deletionResults.Add($"Skipped {mod.Name} (Path not found: {mod.Path})");
                        }
                    }
                    catch (OperationCanceledException) { throw; } // Re-throw cancellation to be caught outside loop
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete {mod.Name}: {ex}");
                        deletionResults.Add($"Failed to delete {mod.Name}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                 cancelled = true;
                 deletionResults.Add("Operation cancelled by user.");
                 Debug.WriteLine("[ExecuteDeleteModsAsync] Deletion loop cancelled.");
            }
            finally
            {
                 if (anySuccess && !cancelled) // Only refresh if successful and not cancelled
                 {
                     RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                 }
                 else
                 {
                     IsLoadingRequest?.Invoke(this, false); // Manually reset if no refresh or cancelled
                 }

                 progressDialog.CompleteOperation(cancelled ? "Deletion Cancelled" : "Deletion Completed");

                 // Show Summary
                 var summaryMessage = new StringBuilder();
                 if (nonDeletableCount > 0) summaryMessage.AppendLine($"{nonDeletableCount} mod(s) were skipped (Core/DLC or missing path).\n");
                 summaryMessage.AppendLine(string.Join("\n", deletionResults));
                 _dialogService.ShowInformation("Deletion Summary", summaryMessage.ToString());
            }
        }

        // --- Multi-Open Methods --- (Synchronous, no change needed)
        private void OpenModFolders(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.Path, "folders", Directory.Exists);
        private void OpenUrls(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.Url, "URLs");
        private void OpenWorkshopPages(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.SteamUrl, "workshop pages");
        private void OpenOtherUrls(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.ExternalUrl, "external URLs");

        private void OpenItems(IList selectedItems, Func<ModItem, string> pathSelector, string itemTypeDescription, Func<string, bool> validator = null)
        {
             // ... implementation remains the same ...
            selectedItems = selectedItems ?? SelectedItems;
            var mods = selectedItems?.Cast<ModItem>().ToList();
            if (mods == null || !mods.Any()) return;


            var opened = new List<string>();
            var notOpened = new List<string>();

            foreach (var mod in mods)
            {
                string target = pathSelector(mod);
                bool isValid = !string.IsNullOrWhiteSpace(target) && (validator == null || validator(target));

                if (isValid)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                        opened.Add(mod.Name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not open {itemTypeDescription} for {mod.Name} ({target}): {ex}");
                        notOpened.Add($"{mod.Name} ({itemTypeDescription}): {ex.Message}");
                    }
                }
                else
                {
                    notOpened.Add($"{mod.Name}: No valid {itemTypeDescription} available or path does not exist");
                }
            }

            if (notOpened.Count > 0)
            {
                var message = $"Opened {itemTypeDescription} for {opened.Count} mods.\n\nCould not open {itemTypeDescription} for:\n" + string.Join("\n", notOpened);
                _dialogService.ShowWarning($"Open {itemTypeDescription.CapitalizeFirst()}", message);
            }
        }


        // --- Analysis/Tool Methods ---
        private async Task ExecuteResolveDependencies(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                // Pass ct if _modListManager method supports it
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                var (addedMods, missingDependencies) = result;

                if (addedMods.Any()) HasUnsavedChangesRequest?.Invoke(this, true);

                var message = new StringBuilder();
                // --- Build Message ---
                if (addedMods.Count > 0)
                {
                    message.AppendLine("The following dependencies were automatically added:");
                    message.AppendLine();
                    foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                    message.AppendLine();
                }
                if (missingDependencies.Count > 0)
                {
                    message.AppendLine("The following dependencies are missing:");
                    message.AppendLine();
                    foreach (var dep in missingDependencies)
                    {
                        message.AppendLine($"- {dep.displayName} ({dep.packageId})");
                        message.AppendLine($"  Required by: {string.Join(", ", dep.requiredBy)}");
                        if (!string.IsNullOrEmpty(dep.steamUrl)) message.AppendLine($"  Workshop URL: {dep.steamUrl}");
                        message.AppendLine();
                    }
                }
                // --- Show Dialog ---
                 RunOnUIThread(() =>
                 {
                     if (message.Length == 0)
                         _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found and no new dependencies were added.");
                     else if (missingDependencies.Count == 0)
                         _dialogService.ShowInformation("Dependencies Added", message.ToString().TrimEnd());
                     else
                         _dialogService.ShowMessageWithCopy("Dependencies Status", message.ToString().TrimEnd(), MessageDialogType.Warning);
                 });

            }
             catch (OperationCanceledException)
            {
                 Debug.WriteLine("[ExecuteResolveDependencies] Operation cancelled.");
                 RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Dependency resolution was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteCheckIncompatibilities(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();

                // Pass ct if service methods support it
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods), ct);
                ct.ThrowIfCancellationRequested(); // Check cancellation after first step

                if (incompatibilities.Count == 0)
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found."));
                    return;
                }

                // Pass ct if service methods support it
                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities), ct);
                ct.ThrowIfCancellationRequested(); // Check cancellation after second step

                if (groups.Count == 0)
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Compatibility Check", "Incompatibilities found but could not be grouped for resolution."));
                    return;
                }

                RunOnUIThread(() => ShowIncompatibilityDialog(groups));
            }
             catch (OperationCanceledException)
            {
                 Debug.WriteLine("[ExecuteCheckIncompatibilities] Operation cancelled.");
                 RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Incompatibility check was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking mod incompatibilities: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Compatibility Error", $"Error checking mod incompatibilities: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        // --- Helper Methods for Dialogs/Callbacks ---
        // These typically remain synchronous or manage their own async if needed internally

        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions,
                () => { /* Cancel handler */ }
            );
            var dialog = new ModIncompatibilityDialogView(dialogViewModel);
            dialog.Owner = Application.Current.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove) // Remains synchronous
        {
            if (modsToRemove == null || modsToRemove.Count == 0) return;

            try
            {
                foreach (var mod in modsToRemove)
                {
                    _modListManager.DeactivateMod(mod);
                }
                HasUnsavedChangesRequest?.Invoke(this, true);
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Resolved incompatibilities by deactivating {modsToRemove.Count} mods.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying incompatibility resolutions: {ex}");
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }


        private void ExecuteCheckDuplicates() // Remains synchronous
        {
            IsLoadingRequest?.Invoke(this, true);
            List<IGrouping<string, ModItem>> actualDuplicateGroups = null;

            try
            {
                var allMods = _modListManager.GetAllMods().ToList();
                actualDuplicateGroups = allMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId))
                    .GroupBy(m => m.PackageId.ToLowerInvariant())
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (actualDuplicateGroups.Any())
                {
                    ShowDuplicateModsDialog(actualDuplicateGroups);
                }
                else
                {
                    _dialogService.ShowInformation("Duplicates Check", "No duplicate mods found based on Package ID.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking duplicates: {ex}");
                _dialogService.ShowError("Duplicates Error", $"Error checking for duplicate mods: {ex.Message}");
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }


        private void ShowDuplicateModsDialog(List<IGrouping<string, ModItem>> duplicateGroups)
        {
            var dialogViewModel = new DuplicateModDialogViewModel(
                    duplicateGroups,
                    pathsToDelete => DeleteDuplicateMods(pathsToDelete), // Note: DeleteDuplicateMods is now async void
                    () => { /* Cancel callback */ });

            var view = new DuplicateModDialogView(dialogViewModel);
            view.Owner = Application.Current.MainWindow;
            view.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            view.ShowDialog();
        }


        // Changed to async void because it's called from a synchronous context (dialog callback)
        // but performs async operations. Proper async handling would require the dialog
        // interaction itself to be awaitable or use a different pattern. Async void is acceptable
        // here as it's a top-level event handler equivalent.
        private async void DeleteDuplicateMods(List<string> pathsToDelete)
        {
            if (pathsToDelete == null || !pathsToDelete.Any()) return;

            IsLoadingRequest?.Invoke(this, true);
            var successfullyDeletedCount = 0;
            var errorMessages = new List<string>();
            bool refreshNeeded = false;

            var allModsLookup = _modListManager.GetAllMods()
                                 .Where(m => !string.IsNullOrEmpty(m.Path))
                                 .ToDictionary(m => m.Path, m => m, StringComparer.OrdinalIgnoreCase);

            // Ideally, show a progress/busy indicator here
            var progressDialog = _dialogService.ShowProgressDialog("Deleting Duplicates", "Starting deletion...", false, true); // Allow cancellation

            using var cts = new CancellationTokenSource();
            // If progressDialog supports cancellation, link it: CancellationTokenSource.CreateLinkedTokenSource(progressDialog.CancellationToken);
            var ct = cts.Token;

            try
            {
                for (int i = 0; i < pathsToDelete.Count; i++)
                {
                     ct.ThrowIfCancellationRequested(); // Check cancellation
                     var path = pathsToDelete[i];
                     if (string.IsNullOrEmpty(path)) continue;

                     allModsLookup.TryGetValue(path, out ModItem modInfo);
                     string modIdentifier = modInfo != null ? $"{modInfo.Name} ({modInfo.PackageId})" : $"'{path}'";

                     progressDialog.Message = $"Deleting {modIdentifier} ({i + 1}/{pathsToDelete.Count})...";
                     progressDialog.UpdateProgress((int)(((double)i + 1) / pathsToDelete.Count * 100.0));

                     try
                     {
                         if (Directory.Exists(path))
                         {
                             // Use Task.Run with cancellation
                             await Task.Run(() => Directory.Delete(path, true), ct);
                             successfullyDeletedCount++;
                             refreshNeeded = true; // Mark that data changed
                             Debug.WriteLine($"[DeleteDuplicateMods] Successfully deleted {modIdentifier}.");
                         }
                         else
                         {
                             Debug.WriteLine($"[DeleteDuplicateMods] Path not found, skipping: {modIdentifier}");
                             errorMessages.Add($"Path not found for {modIdentifier}");
                         }
                     }
                     catch (OperationCanceledException) { throw; } // Propagate cancellation
                     catch (Exception ex)
                     {
                         Debug.WriteLine($"[DeleteDuplicateMods] Error deleting {modIdentifier}: {ex}");
                         errorMessages.Add($"Error deleting {modIdentifier}: {ex.Message}");
                     }
                }
                progressDialog.CompleteOperation("Deletion Completed");
            }
            catch (OperationCanceledException)
            {
                 Debug.WriteLine("[DeleteDuplicateMods] Deletion cancelled by user.");
                 errorMessages.Add("Deletion process was cancelled.");
                 progressDialog.CompleteOperation("Deletion Cancelled");
            }
            catch (Exception ex) // Catch unexpected errors in the loop management
            {
                Debug.WriteLine($"[DeleteDuplicateMods] Unexpected error during batch deletion: {ex}");
                errorMessages.Add($"An unexpected error occurred: {ex.Message}");
                progressDialog.ForceClose(); // Ensure closure on unexpected fault
            }
            finally
            {
                 // --- Completion & Reporting ---
                 var reportTitle = errorMessages.Any() ? "Deletion Complete with Errors/Cancellation" : "Deletion Complete";
                 var reportMessage = new StringBuilder();

                 if (successfullyDeletedCount > 0)
                 {
                     reportMessage.AppendLine($"Successfully deleted {successfullyDeletedCount} duplicate mod folder(s).");
                 }

                 if (errorMessages.Any())
                 {
                     if(reportMessage.Length > 0) reportMessage.AppendLine();
                     reportMessage.AppendLine("Issues encountered:");
                     reportMessage.Append(string.Join("\n -", errorMessages.Prepend(""))); // Indent errors
                 }

                 if(reportMessage.Length == 0)
                 {
                    reportMessage.AppendLine("No duplicate mods were deleted (or the operation was cancelled before any deletion).");
                 }

                // Trigger refresh *after* dialogs are closed and UI is potentially free
                if (refreshNeeded)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Let parent handle loading state during refresh
                }
                else
                {
                    IsLoadingRequest?.Invoke(this, false); // Reset loading if no refresh happened
                }

                 // Show report on UI thread
                 RunOnUIThread(() =>
                 {
                     _dialogService.ShowMessageWithCopy(reportTitle, reportMessage.ToString().Trim(),
                         errorMessages.Any() ? MessageDialogType.Warning : MessageDialogType.Information);
                 });
            }
        }


public async Task ExecuteInstallFromZip(CancellationToken ct = default)
{
    IsLoadingRequest?.Invoke(this, true);
    try
    {
        var fileDialog = new OpenFileDialog
        {
            Filter = "Zip Files (*.zip)|*.zip",
            Title = "Select Mod Zip File"
        };

        if (fileDialog.ShowDialog() != true) return;

        var zipPath = fileDialog.FileName;
        ct.ThrowIfCancellationRequested();

        if (!IsValidZipFile(zipPath))
        {
            _dialogService.ShowError("Invalid File", "The selected file is not a valid zip file.");
            return;
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var modInfo = await ValidateModZip(archive, ct);
            ct.ThrowIfCancellationRequested();

            if (modInfo == null)
            {
                _dialogService.ShowError("Invalid Mod", "The selected zip file doesn't contain a valid RimWorld mod.");
                return;
            }

            var result = _dialogService.ShowConfirmation(
                "Confirm Installation",
                $"Are you sure you want to install the mod '{modInfo.Name}'?",
                showCancel: true);

            if (result != MessageDialogResult.OK) return;
            ct.ThrowIfCancellationRequested();

            var modsPath = _pathService.GetModsPath();
            if (string.IsNullOrEmpty(modsPath))
            {
                _dialogService.ShowError("Path Error", "Mods directory is not set.");
                return;
            }

            // Get root folder entry or null if mod is at root level
            var rootFolder = GetRootModFolder(archive);
            string modName = rootFolder == null 
                ? Path.GetFileNameWithoutExtension(zipPath).Replace(" ", "")
                : rootFolder.FullName.TrimEnd('/', '\\');
                
            var targetDir = Path.Combine(modsPath, modName);

            // Debug what directories actually exist
            Debug.WriteLine($"Mods path: {modsPath}");
            Debug.WriteLine($"Target directory: {targetDir}");
            
            var existingDirs = Directory.GetDirectories(modsPath)
                .Select(Path.GetFileName)
                .ToList();
            
            Debug.WriteLine($"Existing mod directories: {string.Join(", ", existingDirs)}");
            Debug.WriteLine($"Directory exists? {Directory.Exists(targetDir)}");

            // Check if target directory already exists
            if (Directory.Exists(targetDir))
            {
                _dialogService.ShowError("Install Error", 
                    $"A mod folder already exists at:\n{targetDir}");
                return;
            }

            // Validate that folder name doesn't contain invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (rootFolder != null && rootFolder.Name.IndexOfAny(invalidChars) >= 0)
            {
                _dialogService.ShowError("Invalid Name", 
                    $"The mod folder name contains invalid characters: {rootFolder.Name}");
                return;
            }

            // Extract the mod using Task.Run with cancellation
            await Task.Run(() => ExtractMod(archive, targetDir, rootFolder, ct), ct);

            _dialogService.ShowInformation("Install Complete", $"Mod '{modInfo.Name}' was successfully installed.");
            RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Refresh data after install
        }
    }
    catch (OperationCanceledException)
    {
        Debug.WriteLine("[ExecuteInstallFromZip] Installation cancelled.");
        _dialogService.ShowWarning("Installation Cancelled", "Mod installation was cancelled.");
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Error installing mod from zip: {ex}");
        _dialogService.ShowError("Install Error", $"Error installing mod: {ex.Message}");
    }
    finally
    {
        IsLoadingRequest?.Invoke(this, false);
    }
}
        // --- Zip Installation Helpers ---

        private bool IsValidZipFile(string path)
        {
            try
            {
                if (!File.Exists(path) || !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    return false;
                // Check if we can open it and if it has entries
                using (var archive = ZipFile.OpenRead(path))
                {
                    return archive.Entries.Any();
                }
            }
            catch // IOException, InvalidDataException etc.
            {
                return false;
            }
        }

        private async Task<ModItem> ValidateModZip(ZipArchive archive, CancellationToken ct = default)
        {
            // Check root first
            var aboutEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("About/About.xml", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals("About\\About.xml", StringComparison.OrdinalIgnoreCase));

            if (aboutEntry != null)
            {
                return await ParseAboutXmlFromZip(aboutEntry, ct);
            }

            // Check single subfolder
            var rootFolders = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.FullName) && e.FullName.Replace('\\', '/').TrimEnd('/').Contains('/') == false && e.FullName.EndsWith("/"))
                .Select(e => e.FullName.TrimEnd('/', '\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();


            if (rootFolders.Count != 1)
            {
                return null; // No single root folder found
            }

            var rootFolder = rootFolders[0];
            var aboutPathInFolder1 = $"{rootFolder}/About/About.xml";
            var aboutPathInFolder2 = $"{rootFolder}\\About\\About.xml"; // Less common but possible

            aboutEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals(aboutPathInFolder1, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals(aboutPathInFolder2, StringComparison.OrdinalIgnoreCase));

            return aboutEntry != null ? await ParseAboutXmlFromZip(aboutEntry, ct) : null;
        }

        private async Task<ModItem> ParseAboutXmlFromZip(ZipArchiveEntry aboutEntry, CancellationToken ct = default)
        {
            try
            {
                using (var stream = aboutEntry.Open())
                // Use StreamReader that accepts CancellationToken if reading large files,
                // otherwise, ReadToEndAsync doesn't directly support it well here.
                // For typical About.xml, immediate read is fine.
                using (var reader = new StreamReader(stream))
                {
                    ct.ThrowIfCancellationRequested(); // Check before potentially blocking read
                    var content = await reader.ReadToEndAsync();
                    ct.ThrowIfCancellationRequested(); // Check after read

                    var doc = XDocument.Parse(content);
                    var root = doc.Element("ModMetaData");

                    if (root == null) return null;

                    return new ModItem
                    {
                        Name = root.Element("name")?.Value,
                        PackageId = root.Element("packageId")?.Value,
                        Authors = root.Element("author")?.Value ??
                                 string.Join(", ", root.Element("authors")?.Elements("li").Select(x => x.Value) ?? Array.Empty<string>())
                    };
                }
            }
             catch (OperationCanceledException) { throw; } // Propagate cancellation
            catch (Exception ex) // Catch XML parsing errors etc.
            {
                 Debug.WriteLine($"Failed to parse About.xml from zip entry '{aboutEntry.FullName}': {ex.Message}");
                return null;
            }
        }

        private ZipArchiveEntry GetRootModFolder(ZipArchive archive)
{
    var rootFolders = archive.Entries
        .Where(e => !string.IsNullOrEmpty(e.FullName) && 
                e.FullName.Replace('\\', '/').TrimEnd('/').Contains('/') == false && 
                e.FullName.EndsWith("/"))
        .ToList();

    // Debug log for troubleshooting
    Debug.WriteLine($"Found {rootFolders.Count} root folders in zip: {string.Join(", ", rootFolders.Select(f => f.FullName))}");
    
    return rootFolders.Count == 1 ? rootFolders[0] : null;
}


        // Updated to accept and check CancellationToken
private void ExtractMod(ZipArchive archive, string targetDir, ZipArchiveEntry rootFolderEntry, CancellationToken ct)
{
    Debug.WriteLine($"Extracting to target directory: {targetDir}");
    Debug.WriteLine($"Root folder entry: {rootFolderEntry?.FullName ?? "null"}");
    
    Directory.CreateDirectory(targetDir);
    string rootFolderName = rootFolderEntry?.FullName; // e.g., "MyModFolder/"

    foreach (var entry in archive.Entries)
    {
        ct.ThrowIfCancellationRequested();

        // Skip directory entries explicitly
        if (string.IsNullOrEmpty(entry.Name)) continue;

        string relativePath;
        if (rootFolderEntry == null)
        {
            // Archive has mod contents directly in root
            relativePath = entry.FullName;
        }
        else
        {
            // Archive has a single root folder, extract its contents
            if (!entry.FullName.StartsWith(rootFolderName, StringComparison.OrdinalIgnoreCase))
                continue; // Skip files not inside the root folder

            relativePath = entry.FullName.Substring(rootFolderName.Length);
        }

        // Normalize path separators
        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string destinationPath = Path.Combine(targetDir, relativePath);
        
        Debug.WriteLine($"Extracting: {entry.FullName} -> {destinationPath}");

        // Ensure the target directory exists
        var dirPath = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        // Extract the file
        entry.ExtractToFile(destinationPath, overwrite: true);
    }
}

    } // End of ModActionsViewModel class

    // Helper extension (keep or move to a shared location)
    public static class StringExtensions
    {
        public static string CapitalizeFirst(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return char.ToUpper(input[0]) + input.Substring(1);
        }
    }

    // --- Removed Local Command Definitions ---
    // The AsyncRelayCommand and AsyncRelayCommand<T> definitions
    // that were previously here should now be deleted.

} // End of namespace