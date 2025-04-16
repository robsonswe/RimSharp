using RimSharp.Core.Commands;
using RimSharp.MyApp.AppFiles; // For ViewModelBase/RunOnUIThread if needed
using RimSharp.MyApp.Dialogs; // For MessageDialogResult
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
            // Use CreateCancellableAsyncCommand/CreateCommand and observe relevant properties

            ClearActiveListCommand = CreateCancellableAsyncCommand(
                ExecuteClearActiveList,
                CanExecuteSimpleCommands, // Depends on IsParentLoading
                nameof(IsParentLoading));

            SortActiveListCommand = CreateCancellableAsyncCommand(
                ExecuteSortActiveList,
                CanExecuteSimpleCommands, // Depends on IsParentLoading
                nameof(IsParentLoading));

            SaveCommand = CreateCommand(
                ExecuteSaveMods,
                CanExecuteSaveMods, // Depends on HasUnsavedChanges, IsParentLoading
                nameof(HasUnsavedChanges), nameof(IsParentLoading));

            ImportListCommand = CreateCancellableAsyncCommand(
                ExecuteImport,
                CanExecuteSimpleCommands, // Depends on IsParentLoading
                nameof(IsParentLoading));

            ExportListCommand = CreateCancellableAsyncCommand(
                ExecuteExport,
                CanExecuteExport, // Depends on IsParentLoading and list content
                nameof(IsParentLoading)); // Also depends on _modListManager state, manual update might be needed via ListChanged

            // For ExportListCommand, we need to re-evaluate when the list changes.
            // The ListChanged event handler in ModsViewModel calls CommandManager.InvalidateRequerySuggested,
            // which should cause this command to re-evaluate its CanExecute.
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
                HasUnsavedChangesRequest?.Invoke(this, false); // <<< KEEP/ENSURE THIS IS HERE

                Debug.WriteLine("Mod list saved successfully.");
                RunOnUIThread(() => _dialogService.ShowInformation("Save Successful", "Current active mod list has been saved."));
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
