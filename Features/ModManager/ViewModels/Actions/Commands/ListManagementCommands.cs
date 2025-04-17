using RimSharp.Core.Commands;
using RimSharp.AppDir.AppFiles; // For ViewModelBase/RunOnUIThread if needed
using RimSharp.AppDir.Dialogs; // For MessageDialogResult
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

            SaveCommand = CreateCommand(
                ExecuteSaveMods,
                CanExecuteSaveMods,
                // Observe all relevant properties for CanExecuteSaveMods
                observedProperties: new[] { nameof(HasUnsavedChanges), nameof(IsParentLoading) }); // Save doesn't depend on paths, only unsaved changes and loading state

            ImportListCommand = CreateCancellableAsyncCommand(
                ExecuteImport,
                CanExecuteSimpleCommands,
                // OBSERVE BOTH IsParentLoading AND HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            // ExportListCommand's CanExecuteExport depends on list content, not HasValidPaths.
            // It relies on CommandManager.InvalidateRequerySuggested triggered by ListChanged, which is fine.
            ExportListCommand = CreateCancellableAsyncCommand(
                ExecuteExport,
                CanExecuteExport,
                observedProperties: new[] { nameof(IsParentLoading) }); // Primarily depends on loading state and list content (handled by InvalidateRequerySuggested)

            // CheckReplacementsCommand already observes HasValidPaths, keep as is or ensure it's correct:
            CheckReplacementsCommand = CreateCancellableAsyncCommand(
                execute: ExecuteCheckReplacements,
                canExecute: CanExecuteSimpleCommands, // Uses HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) }); // Already correctly observes both
        }


        // --- CanExecute Predicates (used by command creation) ---
        // private bool CanExecuteSimpleCommands() => !IsParentLoading; // Defined in InstallationCommands.cs
        private bool CanExecuteSaveMods()
        {
            bool canExecute = HasUnsavedChanges && !IsParentLoading;
            Debug.WriteLine($"[ModActionsViewModel] CanExecuteSaveMods Check: Result={canExecute} (HasUnsavedChanges: {HasUnsavedChanges}, IsParentLoading: {IsParentLoading})");
            return canExecute;
        }
        private bool CanExecuteExport() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();


        // --- Execution Methods ---
        // Signature changed for async commands to accept CancellationToken
        private async Task ExecuteClearActiveList(CancellationToken ct)
        {
            // CanExecute checked by framework
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                // Check token before long operation
                ct.ThrowIfCancellationRequested();
                await Task.Run(() => _modListManager.ClearActiveList(), ct); // Assume ClearActiveList respects token if possible
                HasUnsavedChangesRequest?.Invoke(this, true); // Signal parent
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteClearActiveList] Operation cancelled.");
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Clearing the list was cancelled."));
            }
            catch (Exception ex) // Catch potential exceptions from ClearActiveList
            {
                Debug.WriteLine($"[ExecuteClearActiveList] Error: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Clear Error", $"Failed to clear active list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteSortActiveList(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            bool orderChanged = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                orderChanged = await Task.Run(() => _modListManager.SortActiveList(), ct); // Assume Sort respects token if possible

                ct.ThrowIfCancellationRequested(); // Check after sort completes

                // Show result dialog on UI thread
                await RunOnUIThreadAsync(() =>
                {
                    if (orderChanged)
                        _dialogService.ShowInformation("Sort Complete", "Active mods sorted successfully.");
                    else
                        _dialogService.ShowInformation("Sort Complete", "Mods already in correct order.");
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteSortActiveList] Sorting cancelled.");
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Sorting was cancelled."));
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
            if (_modListManager.HasAnyActiveModIssues)
            {
                var confirmationResult = _dialogService.ShowConfirmation(
                    "Save Warning",
                    "The active mod list has potential issues (e.g., missing dependencies, load order conflicts, incompatibilities).\n\n" +
                    "Saving the list in this state might cause problems when running the game.\n\n" +
                    "Do you want to save anyway?",
                    showCancel: true); // Show OK (Yes) and Cancel (No)

                if (confirmationResult == MessageDialogResult.Cancel) // User chose not to save
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

                _dataService.SaveActiveModIdsToConfig(activeIdsToSave); // Assume synchronous

                // Signal parent that changes are now saved
                HasUnsavedChangesRequest?.Invoke(this, false);

                Debug.WriteLine("Mod list saved successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving mods config: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Save Error", $"Failed to save mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }
        private async Task ExecuteImport(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                // Assuming ImportModListAsync now accepts and respects CancellationToken
                await _ioService.ImportModListAsync(); // TODO: Update IModListIOService if cancellation needed
                ct.ThrowIfCancellationRequested(); // Check after completion
                                                   // Refresh is likely needed after import - parent VM handles refresh logic
                RequestDataRefresh?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteImport] Import cancelled.");
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Mod list import was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error importing list: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Import Error", $"Failed to import mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteExport(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                var activeModsToExport = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                if (!activeModsToExport.Any())
                {
                    // Use RunOnUIThread for dialog if CanExecute might race
                    RunOnUIThread(() => _dialogService.ShowInformation("Export List", "There are no active mods to export."));
                    return;
                }
                ct.ThrowIfCancellationRequested();
                // Assuming ExportModListAsync now accepts and respects CancellationToken
                await _ioService.ExportModListAsync(activeModsToExport); // TODO: Update IModListIOService if cancellation needed
                ct.ThrowIfCancellationRequested();
                // Show success message on UI thread
                RunOnUIThread(() => _dialogService.ShowInformation("Export Complete", "Active mod list exported successfully."));
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteExport] Export cancelled.");
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Mod list export was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting list: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Export Error", $"Failed to export mod list: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }
    }
}
