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
            ClearActiveListCommand = new AsyncRelayCommand(ExecuteClearActiveList, CanExecuteSimpleCommands);
            SortActiveListCommand = new AsyncRelayCommand(ExecuteSortActiveList, CanExecuteSimpleCommands);
            SaveCommand = new RelayCommand(ExecuteSaveMods, CanExecuteSaveMods);
            ImportListCommand = new AsyncRelayCommand(ExecuteImport, CanExecuteSimpleCommands);
            ExportListCommand = new AsyncRelayCommand(ExecuteExport, CanExecuteExport);
        }

        // --- Execution Methods ---
        private async Task ExecuteClearActiveList(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                await _commandService.ClearActiveModsAsync(); // Assuming ct support added if needed
                HasUnsavedChangesRequest?.Invoke(this, true);
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private async Task ExecuteSortActiveList(CancellationToken ct = default)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                await _commandService.SortActiveModsAsync(); // Assuming ct support added if needed
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
                await _ioService.ImportModListAsync(); // Assuming ct support added if needed
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
                await _ioService.ExportModListAsync(activeModsToExport); // Assuming ct support added if needed
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