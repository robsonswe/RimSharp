using RimSharp.Core.Commands;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Features.ModManager.Dialogs.ActiveIssues;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        // Partial initialization method
        private void InitializeListManagementCommands()
        {
            ClearActiveListCommand = CreateCancellableAsyncCommand(
                ExecuteClearActiveList,
                CanExecuteSimpleCommands,
                // OBSERVE BOTH IsParentLoading AND HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            SortActiveListCommand = CreateCancellableAsyncCommand(
                ExecuteSortActiveList,
                CanExecuteSimpleCommands,
                // OBSERVE BOTH IsParentLoading AND HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            SaveCommand = CreateAsyncCommand(
                () => ExecuteSaveMods(false), // Default: Don't suppress warnings
                CanExecuteSaveMods,
                // Observe all relevant properties for CanExecuteSaveMods
                observedProperties: new[] { nameof(HasUnsavedChanges), nameof(IsParentLoading) }); // Save doesn't depend on paths, only unsaved changes and loading state

            ImportListCommand = CreateCancellableAsyncCommand(
                ExecuteImport,
                CanExecuteSimpleCommands,
                // OBSERVE BOTH IsParentLoading AND HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            // ExportListCommand's CanExecuteExport depends on list content, not HasValidPaths..
            ExportListCommand = CreateCancellableAsyncCommand(
                ExecuteExport,
                CanExecuteExport,
                observedProperties: new[] { nameof(IsParentLoading) }); // Primarily depends on loading state and list content (handled by InvalidateRequerySuggested)

            CheckReplacementsCommand = CreateCancellableAsyncCommand(
                execute: ExecuteCheckReplacements,
                canExecute: CanExecuteSimpleCommands, // Uses HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) }); // Already correctly observes both
        }

        private bool CanExecuteSaveMods()
        {
            bool canExecute = HasUnsavedChanges && !IsParentLoading;
            Debug.WriteLine($"[ModActionsViewModel] CanExecuteSaveMods Check: Result={canExecute} (HasUnsavedChanges: {HasUnsavedChanges}, IsParentLoading: {IsParentLoading})");
            return canExecute;
        }
        private bool CanExecuteExport() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();
        private async Task ExecuteClearActiveList(CancellationToken ct)
        {
            // CanExecute checked by framework
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                await Task.Run(() => _modListManager.ClearActiveList(), ct); // Assume ClearActiveList respects token if possible
                HasUnsavedChangesRequest?.Invoke(this, true); 
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteClearActiveList] Operation cancelled.");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Clearing the list was cancelled."));
            }
            catch (Exception ex) // Catch potential exceptions from ClearActiveList
            {
                Debug.WriteLine($"[ExecuteClearActiveList] Error: {ex}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Clear Error", $"Failed to clear active list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteSortActiveList(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null;
            bool orderChanged = false;
            try
            {
                ct.ThrowIfCancellationRequested();

                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                        "Sorting Mods",
                        "Analyzing dependencies and determining optimal order...",
                        canCancel: true,
                        isIndeterminate: true,
                        cts: null); // We'll link it below
                });

                if (progressDialog == null) throw new InvalidOperationException("Could not create progress dialog.");

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                combinedToken.ThrowIfCancellationRequested();
                orderChanged = await Task.Run(() => _modListManager.SortActiveList(), combinedToken); // Assume Sort respects token if possible

                combinedToken.ThrowIfCancellationRequested(); // Check after sort completes

                progressDialog.CompleteOperation("Sorting complete.");
                await RunOnUIThreadAsync(async () =>
                {
                    if (orderChanged)
                        await _dialogService.ShowInformation("Sort Complete", "Active mods sorted successfully.");
                    else
                        await _dialogService.ShowInformation("Sort Complete", "Mods already in correct order.");
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteSortActiveList] Sorting cancelled.");
                progressDialog?.ForceClose();
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Sorting was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sorting mods: {ex}");
                progressDialog?.ForceClose();
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Sort Error", $"Error sorting mods: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteSaveMods(bool suppressWarnings = false)
        {
            if (!suppressWarnings && _modListManager.HasAnyActiveModIssues)
            {
                var issues = _modListManager.GetActiveModIssues();
                var msg = "The active mod list has potential issues (e.g., missing dependencies, load order conflicts, incompatibilities).\n\n" +
                          "Saving the list in this state might cause problems when running the game.\n\n" +
                          "Do you want to save anyway?";

                var vm = new ActiveIssuesDialogViewModel(issues, msg);
                bool confirmed = await _dialogService.ShowActiveIssuesDialogAsync(vm);

                if (!confirmed) // User chose not to save
                {
                    Debug.WriteLine("Save cancelled by user due to detected issues.");
                    return; // Exit without saving
                }
                Debug.WriteLine("User confirmed saving despite issues.");
            }

            // CanExecute checked by framework
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var activeIdsToSave = _modListManager.VirtualActiveMods
                                        .Select(entry => entry.Mod.PackageId)
                                        .Where(id => !string.IsNullOrEmpty(id))
                                        .ToList();

                await Task.Run(() => _dataService.SaveActiveModIdsToConfig(activeIdsToSave)); 
                HasUnsavedChangesRequest?.Invoke(this, false);

                Debug.WriteLine("Mod list saved successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mods config: {ex}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Save Error", $"Failed to save mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }
        private async Task ExecuteImport(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();

                await _ioService.ImportModListAsync(); // TODO: Update IModListIOService if cancellation needed
                ct.ThrowIfCancellationRequested(); // Check after completion

                RequestDataRefresh?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteImport] Import cancelled.");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Mod list import was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error importing list: {ex}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Import Error", $"Failed to import mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteExport(CancellationToken ct)
        {
            // CanExecute checked by framework
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                var activeModsToExport = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                if (!activeModsToExport.Any())
                {

                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Export List", "There are no active mods to export."));
                    return;
                }
                ct.ThrowIfCancellationRequested();

                await _ioService.ExportModListAsync(activeModsToExport); // TODO: Update IModListIOService if cancellation needed
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteExport] Export cancelled.");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Mod list export was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting list: {ex}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Export Error", $"Failed to export mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }
    }
}


