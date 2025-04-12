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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input; // For Application.Current etc.

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
        public ICommand CreatePackCommand { get; private set; }
        public ICommand FixIntegrityCommand { get; private set; }
        public ICommand RunGameCommand { get; private set; }


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
            IDialogService dialogService)
        {
            _dataService = dataService;
            _commandService = commandService;
            _ioService = ioService;
            _modListManager = modListManager;
            _incompatibilityService = incompatibilityService;
            _dialogService = dialogService;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            ClearActiveListCommand = new AsyncRelayCommand(ExecuteClearActiveList, CanExecuteSimpleCommands);
            SortActiveListCommand = new AsyncRelayCommand(ExecuteSortActiveList, CanExecuteSimpleCommands);
            SaveCommand = new RelayCommand(ExecuteSaveMods, CanExecuteSaveMods);
            ImportListCommand = new AsyncRelayCommand(ExecuteImport, CanExecuteSimpleCommands);
            ExportListCommand = new AsyncRelayCommand(ExecuteExport, CanExecuteExport);
            ResolveDependenciesCommand = new AsyncRelayCommand(ExecuteResolveDependencies, CanExecuteSimpleCommands);
            CheckIncompatibilitiesCommand = new AsyncRelayCommand(ExecuteCheckIncompatibilities, CanExecuteCheckIncompatibilities);
            CheckDuplicatesCommand = new RelayCommand(ExecuteCheckDuplicates, CanExecuteSimpleCommands);

            DeleteModCommand = new AsyncRelayCommand<ModItem>(ExecuteDeleteModAsync, CanExecuteDeleteMod);
            DeleteModsCommand = new AsyncRelayCommand<IList>(ExecuteDeleteModsAsync, CanExecuteDeleteMods);

            OpenModFoldersCommand = new RelayCommand<IList>(OpenModFolders, CanExecuteMultiSelectActions);
            OpenUrlsCommand = new RelayCommand<IList>(OpenUrls, CanExecuteMultiSelectActions);
            OpenWorkshopPagesCommand = new RelayCommand<IList>(OpenWorkshopPages, CanExecuteMultiSelectActions);
            OpenOtherUrlsCommand = new RelayCommand<IList>(OpenOtherUrls, CanExecuteMultiSelectActions);

            // Placeholders
            StripModsCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Strip mods: Functionality not yet implemented."), _ => !IsParentLoading);
            CreatePackCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Create pack: Functionality not yet implemented."), _ => !IsParentLoading);
            FixIntegrityCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."), _ => !IsParentLoading);
            RunGameCommand = new RelayCommand(_ => _dialogService.ShowInformation("Not Implemented", "Run game: Functionality not yet implemented."), _ => !IsParentLoading);
        }

        private void RaiseCanExecuteChangedForAllCommands()
        {
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
            (CreatePackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FixIntegrityCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RunGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // --- CanExecute Methods ---

        private bool CanExecuteSimpleCommands() => !IsParentLoading;
        private bool CanExecuteSaveMods() => HasUnsavedChanges && !IsParentLoading;
        private bool CanExecuteExport() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();
        private bool CanExecuteCheckIncompatibilities() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();

        private bool CanExecuteDeleteMod(ModItem mod)
        {
            // If command parameter is null, try using the ViewModel's SelectedMod
            mod = mod ?? SelectedMod;
            return mod != null
                && mod.ModType != ModType.Core
                && mod.ModType != ModType.Expansion
                && !string.IsNullOrEmpty(mod.Path)
                && Directory.Exists(mod.Path) // Check existence here or rely on Execute? Check is safer.
                && !IsParentLoading;
        }

        private bool CanExecuteDeleteMods(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems; // Use property if parameter is null
            return selectedItems != null
                && selectedItems.Count > 0
                && selectedItems.Cast<ModItem>().Any(CanBeDeleted) // Check if at least one is potentially deletable
                && !IsParentLoading;
        }

        private bool CanExecuteMultiSelectActions(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems; // Use property if parameter is null
            return selectedItems != null && selectedItems.Count > 0 && !IsParentLoading;
        }


        // Helper for deletable checks
        private bool CanBeDeleted(ModItem mod) => mod != null && mod.ModType != ModType.Core && mod.ModType != ModType.Expansion && !string.IsNullOrEmpty(mod.Path); // Simplified check

        // --- Execution Methods (mostly copied/adapted from original ModsViewModel) ---

        private async Task ExecuteClearActiveList()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                await _commandService.ClearActiveModsAsync();
                HasUnsavedChangesRequest?.Invoke(this, true); // Clearing changes the list
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteSortActiveList()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                await _commandService.SortActiveModsAsync();
                // ModListManager will fire ListChanged if order actually changed, parent handles HasUnsavedChanges
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sorting mods: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Sort Error", $"Error sorting mods: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ExecuteSaveMods()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeIdsToSave = _modListManager.VirtualActiveMods
                                        .Select(entry => entry.Mod.PackageId)
                                        .Where(id => !string.IsNullOrEmpty(id))
                                        .ToList();

                _dataService.SaveActiveModIdsToConfig(activeIdsToSave);
                HasUnsavedChangesRequest?.Invoke(this, false); // Save clears the flag
                Debug.WriteLine("Mod list saved successfully.");
                _dialogService.ShowInformation("Save Successful", "Current active mod list has been saved."); // Optional feedback
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mods config: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Save Error", $"Failed to save mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteImport()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                await _ioService.ImportModListAsync();
                // ModListManager should fire ListChanged if import succeeded, parent handles HasUnsavedChanges
            }
            catch (Exception ex) // Catch exceptions specific to IO service if possible
            {
                Debug.WriteLine($"Error importing list: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Import Error", $"Failed to import mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteExport()
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
                await _ioService.ExportModListAsync(activeModsToExport);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting list: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Export Error", $"Failed to export mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }


        private async Task ExecuteDeleteModAsync(ModItem mod)
        {
            mod = mod ?? SelectedMod; // Use property if parameter is null
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
                await Task.Run(() => Directory.Delete(mod.Path, recursive: true));
                deletionSuccess = true;
                _dialogService.ShowInformation("Deletion Successful", $"Mod '{mod.Name}' was deleted.");
            }
            catch (Exception ex) // Catch specific exceptions like UnauthorizedAccessException, IOException
            {
                Debug.WriteLine($"[ExecuteDeleteModAsync] Error deleting '{mod.Path}': {ex}");
                _dialogService.ShowError("Deletion Error", $"Could not delete mod '{mod.Name}'.\nError: {ex.Message}");
            }
            finally
            {
                if (deletionSuccess)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Ask parent to refresh
                                                                       // IsLoading will be set to false by parent after refresh
                }
                else
                {
                    IsLoadingRequest?.Invoke(this, false); // Reset loading if deletion failed before refresh
                }
            }
        }

        private async Task ExecuteDeleteModsAsync(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems; // Use property if parameter is null
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
            if (nonDeletableCount > 0)
            {
                confirmationMessage += $"\n\n({nonDeletableCount} selected mod(s) like Core/DLC cannot be deleted and will be skipped).";
            }
            confirmationMessage += "\n\nThis action cannot be undone.";


            var result = _dialogService.ShowConfirmation("Confirm Deletion", confirmationMessage, showCancel: true);
            if (result != MessageDialogResult.OK) return;


            // --- Deletion Logic with Progress ---
            var progressDialog = _dialogService.ShowProgressDialog(
               "Deleting Mods",
               $"Preparing to delete {deletableMods.Count} mods...",
               canCancel: true); // Allow cancellation if needed

            IsLoadingRequest?.Invoke(this, true);
            var deletionResults = new List<string>();
            bool cancelled = false;
            bool anySuccess = false;

            for (int i = 0; i < deletableMods.Count; i++)
            {
                var mod = deletableMods[i];
                if (progressDialog.DialogResult == false) // Check for cancellation
                {
                    cancelled = true;
                    deletionResults.Add("Operation cancelled by user.");
                    break;
                }

                progressDialog.Message = $"Deleting {mod.Name} ({i + 1}/{deletableMods.Count})...";
                progressDialog.UpdateProgress((int)((double)(i + 1) / deletableMods.Count * 100.0));


                try
                {
                    if (Directory.Exists(mod.Path)) // Double check existence before delete attempt
                    {
                        await Task.Run(() => Directory.Delete(mod.Path, recursive: true));
                        deletionResults.Add($"Successfully deleted {mod.Name}");
                        anySuccess = true;
                    }
                    else
                    {
                        deletionResults.Add($"Skipped {mod.Name} (Path not found: {mod.Path})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete {mod.Name}: {ex}");
                    deletionResults.Add($"Failed to delete {mod.Name}: {ex.Message}");
                }
            }

            // --- Completion ---
            if (anySuccess)
            {
                RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Refresh if anything was deleted
                // Parent handles IsLoading=false after refresh
            }
            else
            {
                IsLoadingRequest?.Invoke(this, false); // Manually reset if no refresh triggered
            }

            progressDialog.CompleteOperation(cancelled ? "Deletion Cancelled" : "Deletion Completed");

            // Show Summary
            var summaryMessage = new StringBuilder();
            if (nonDeletableCount > 0) summaryMessage.AppendLine($"{nonDeletableCount} mod(s) were skipped (Core/DLC or missing path).\n");
            summaryMessage.AppendLine(string.Join("\n", deletionResults));
            _dialogService.ShowInformation("Deletion Summary", summaryMessage.ToString());
        }

        // --- Multi-Open Methods ---
        private void OpenModFolders(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.Path, "folders", Directory.Exists);
        private void OpenUrls(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.Url, "URLs");
        private void OpenWorkshopPages(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.SteamUrl, "workshop pages");
        private void OpenOtherUrls(IList selectedItems) => OpenItems(selectedItems ?? SelectedItems, m => m.ExternalUrl, "external URLs");

        private void OpenItems(IList selectedItems, Func<ModItem, string> pathSelector, string itemTypeDescription, Func<string, bool> validator = null)
        {
            selectedItems = selectedItems ?? SelectedItems; // Use property if parameter is null
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
            else if (opened.Count > 0)
            {
                // Optional: Show success message if all opened?
                //_dialogService.ShowInformation($"Open {itemTypeDescription.CapitalizeFirst()}", $"Opened {itemTypeDescription} for {opened.Count} mods.");
            }
        }

        // --- Analysis/Tool Methods ---
        private async Task ExecuteResolveDependencies()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var result = await Task.Run(() => _modListManager.ResolveDependencies());
                var (addedMods, missingDependencies) = result;

                if (addedMods.Any()) HasUnsavedChangesRequest?.Invoke(this, true); // Changes were made

                // Display Results (copied logic)
                var message = new StringBuilder();
                // ... (build message as before) ...
                if (message.Length == 0) { /* Show No Issues dialog */ }
                else { /* Show Resolution dialog */ }
                // Copied from original ModsViewModel:
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }


        private async Task ExecuteCheckIncompatibilities()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods));

                if (incompatibilities.Count == 0)
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found."));
                    return;
                }

                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities));
                if (groups.Count == 0)
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Compatibility Check", "Incompatibilities found but could not be grouped for resolution.")); // Edge case?
                    return;
                }

                RunOnUIThread(() => ShowIncompatibilityDialog(groups)); // Uses ApplyIncompatibilityResolutions callback
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking mod incompatibilities: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Compatibility Error", $"Error checking mod incompatibilities: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        // --- Helper Methods for Dialogs/Callbacks ---

        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions, // Callback on OK
                () => { /* Cancel handler if needed */ }
            );
            var dialog = new ModIncompatibilityDialogView(dialogViewModel) { /* Set Owner/Startup */ };
            dialog.Owner = Application.Current.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove)
        {
            if (modsToRemove == null || modsToRemove.Count == 0) return;

            try
            {
                foreach (var mod in modsToRemove)
                {
                    _modListManager.DeactivateMod(mod); // Let manager handle removal
                }
                HasUnsavedChangesRequest?.Invoke(this, true); // Changes were made
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Resolved incompatibilities by deactivating {modsToRemove.Count} mods.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying incompatibility resolutions: {ex}");
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }


        private void ExecuteCheckDuplicates()
        {
            IsLoadingRequest?.Invoke(this, true);
            List<IGrouping<string, ModItem>> actualDuplicateGroups = null; // Store the correct type

            try
            {
                var allMods = _modListManager.GetAllMods().ToList();

                // Perform grouping but keep the IGrouping<string, ModItem> structure
                actualDuplicateGroups = allMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId)) // Ensure PackageId exists
                    .GroupBy(m => m.PackageId.ToLowerInvariant())    // Group by PackageId
                    .Where(g => g.Count() > 1)                     // Where PackageId has duplicates
                    .ToList(); // List of groups based on PackageId

                if (actualDuplicateGroups.Any())
                {
                    // Now, pass the correctly typed list to the dialog ViewModel constructor
                    ShowDuplicateModsDialog(actualDuplicateGroups); // Uses DeleteDuplicateMods callback
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
            // The constructor now receives the correct type
            var dialogViewModel = new DuplicateModDialogViewModel(
                    duplicateGroups,
                    pathsToDelete => DeleteDuplicateMods(pathsToDelete), // Callback on OK
                    () => { /* Cancel callback */ });

            var view = new DuplicateModDialogView(dialogViewModel) { /* Set Owner/Startup */ };
            view.Owner = Application.Current.MainWindow;
            view.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            view.ShowDialog();
        }


        private async void DeleteDuplicateMods(List<string> pathsToDelete)
        {
            if (pathsToDelete == null || !pathsToDelete.Any()) return;

            IsLoadingRequest?.Invoke(this, true); // Deletion takes time
            var successfullyDeletedCount = 0;
            var errorMessages = new List<string>();

            // Create a lookup for better reporting
            var allModsLookup = _modListManager.GetAllMods()
                                 .Where(m => !string.IsNullOrEmpty(m.Path))
                                 .ToDictionary(m => m.Path, m => m, StringComparer.OrdinalIgnoreCase);


            foreach (var path in pathsToDelete)
            {
                if (string.IsNullOrEmpty(path)) continue; // Skip invalid paths

                allModsLookup.TryGetValue(path, out ModItem modInfo);
                string modIdentifier = modInfo != null ? $"{modInfo.Name} ({modInfo.PackageId})" : $"'{path}'";

                try
                {
                    if (Directory.Exists(path))
                    {
                        await Task.Run(() => Directory.Delete(path, true));
                        successfullyDeletedCount++;
                        Debug.WriteLine($"[DeleteDuplicateMods] Successfully deleted {modIdentifier}.");
                    }
                    else
                    {
                        Debug.WriteLine($"[DeleteDuplicateMods] Path not found, skipping: {modIdentifier}");
                        errorMessages.Add($"Path not found for {modIdentifier}");
                    }
                }
                catch (Exception ex) // Catch specific IO/Auth errors
                {
                    Debug.WriteLine($"[DeleteDuplicateMods] Error deleting {modIdentifier}: {ex}");
                    errorMessages.Add($"Error deleting {modIdentifier}: {ex.Message}");
                }
            }

            // --- Completion & Reporting ---
            var reportTitle = errorMessages.Any() ? "Deletion Complete with Errors" : "Deletion Complete";
            var reportMessage = new StringBuilder();

            if (successfullyDeletedCount > 0)
            {
                reportMessage.AppendLine($"Successfully deleted {successfullyDeletedCount} duplicate mod folder(s).");
                reportMessage.AppendLine();
                RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Refresh if deletions occurred
                                                                   // Parent handles IsLoading=false after refresh
            }
            else if (!errorMessages.Any())
            {
                reportMessage.AppendLine("No duplicate mods were deleted (perhaps paths were already gone?).");
                IsLoadingRequest?.Invoke(this, false); // No refresh needed, reset loading manually
            }
            else
            {
                // Only errors occurred
                IsLoadingRequest?.Invoke(this, false); // No refresh needed, reset loading manually
            }


            if (errorMessages.Any())
            {
                reportMessage.AppendLine("Errors encountered during deletion:");
                reportMessage.Append(string.Join("\n -", errorMessages.Prepend(""))); // Indent errors
                reportMessage.AppendLine();
            }

            RunOnUIThread(() =>
            {
                _dialogService.ShowMessageWithCopy(reportTitle, reportMessage.ToString().Trim(),
                    errorMessages.Any() ? MessageDialogType.Warning : MessageDialogType.Information);
            });
        }

    }

    // Helper extension (can be in a separate file)
    public static class StringExtensions
    {
        public static string CapitalizeFirst(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return char.ToUpper(input[0]) + input.Substring(1);
        }
    }

    // Need AsyncRelayCommand if not already defined
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && _canExecute();
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    RaiseCanExecuteChanged();
                    await _execute();
                }
                finally
                {
                    _isExecuting = false;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<T, bool> _canExecute;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (_ => true);
        }

        public bool CanExecute(object parameter)
        {
            // Check type compatibility before calling _canExecute
            if (parameter != null && !(parameter is T) && typeof(T).IsValueType)
                return false; // Cannot execute if parameter type mismatch for value types

            // Allow execution if parameter is null and T is nullable, or if parameter is null/assignable for reference types
            bool canAttemptExecute = (parameter == null && !typeof(T).IsValueType) || (parameter == null && Nullable.GetUnderlyingType(typeof(T)) != null) || (parameter is T);

            return !_isExecuting && canAttemptExecute && _canExecute((T)parameter);

        }

        public async void Execute(object parameter)
        {
            // Ensure CanExecute would allow this before proceeding
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    RaiseCanExecuteChanged();
                    // Parameter was already checked for type compatibility in CanExecute
                    await _execute((T)parameter);
                }
                finally
                {
                    _isExecuting = false;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}