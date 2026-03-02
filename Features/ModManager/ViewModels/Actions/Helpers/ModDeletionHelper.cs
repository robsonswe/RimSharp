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

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        
        private bool CanBeDeleted(ModItem mod) =>
            mod != null &&
            mod.ModType != ModType.Core &&
            mod.ModType != ModType.Expansion &&
            !string.IsNullOrEmpty(mod.Path); 
        private async Task DeleteSingleModAsyncInternal(ModItem? mod, CancellationToken ct = default)
        {
            mod ??= SelectedMod;

            // Re-check CanExecute conditions specifically for this action
            if (mod == null || !CanBeDeleted(mod))
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Precondition failed for mod '{mod?.Name}'.");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Deletion Blocked", $"Cannot delete mod '{mod?.Name}'. It might be Core/DLC or essential information is missing."));
                return;
            }
             if (!Directory.Exists(mod.Path))
            {
                 Debug.WriteLine($"[DeleteSingleModAsyncInternal] Path not found for mod '{mod.Name}': {mod.Path}");
                 await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Deletion Skipped", $"The folder for mod '{mod.Name}' does not exist or was already deleted.\nPath: {mod.Path}"));
                 RequestDataRefresh?.Invoke(this, EventArgs.Empty); 
                 return;
            }
            bool hasDuplicate = _modListManager.GetAllMods().Count(m => string.Equals(m.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase)) > 1;
            List<ModItem> dependingActiveMods = _modListManager.GetActiveModsDependingOn(mod.PackageId);
            bool warningSuppressionNeeded = false;

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine($"Are you sure you want to permanently delete the mod '{mod.Name}'?");
            messageBuilder.AppendLine("\nThis action cannot be undone.");
            messageBuilder.AppendLine($"\nPath: {mod.Path}");

            if (!hasDuplicate && dependingActiveMods.Any())
            {
                messageBuilder.AppendLine("\nWARNING: This mod is currently active and required by the following active mods:");
                foreach(var dep in dependingActiveMods.Take(5)) messageBuilder.AppendLine($" - {dep.Name}");
                if (dependingActiveMods.Count > 5) messageBuilder.AppendLine($" - ...and {dependingActiveMods.Count - 5} others.");
                messageBuilder.AppendLine("\nDeleting this mod will break your active mod list.");
                warningSuppressionNeeded = true;
            }
            else if (hasDuplicate)
            {
                messageBuilder.AppendLine("\nNote: A duplicate version of this mod was detected. The game will automatically use the remaining version.");
            }

            var result = await _dialogService.ShowConfirmationAsync(
               "Confirm Deletion",
               messageBuilder.ToString(),
               showCancel: true);

            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            IsLoadingRequest?.Invoke(this, true);
            bool deletionSuccess = false;
            try
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Attempting to delete directory: {mod.Path}");
                await _deletionService.DeleteDirectoryRobustAsync(mod.Path, ct);
                deletionSuccess = true;
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Successfully deleted: {mod.Path}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Deletion Successful", $"Mod '{mod.Name}' was deleted."));
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Deletion cancelled for '{mod.Path}'.");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Deletion Cancelled", $"Deletion of mod '{mod.Name}' was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Error deleting '{mod.Path}': {ex}");
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Deletion Error", $"Could not delete mod '{mod.Name}'.\nError: {ex.Message}\nPath: {mod.Path}"));
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
                if (deletionSuccess && mod != null)
                {
                    await HandlePostDeletionAsync(new[] { mod }, warningSuppressionNeeded);
                }
            }
        }

        private async Task DeleteMultipleModsAsyncInternal(IList? selectedItems, CancellationToken ct = default)
        {
            selectedItems ??= SelectedItems;
            var mods = selectedItems?.Cast<ModItem>().ToList();

            if (mods == null || !mods.Any())
            {
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Deletion Blocked", "No mods selected for deletion."));
                return;
            }

            var deletableMods = mods.Where(CanBeDeleted).ToList();
            var nonDeletableCount = mods.Count - deletableMods.Count;

            if (deletableMods.Count == 0)
            {
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Deletion Blocked", "None of the selected mods can be deleted (Core/DLC or missing essential info)."));
                return;
            }

            var confirmationMessage = $"Are you sure you want to permanently delete {deletableMods.Count} mod(s)?";
            if (nonDeletableCount > 0)
            {
                 confirmationMessage += $"\n({nonDeletableCount} selected mod(s) like Core/DLC cannot be deleted and will be skipped).";
            }
            confirmationMessage += "\n\nThis action cannot be undone.";

            var result = await _dialogService.ShowConfirmationAsync("Confirm Deletion", confirmationMessage, showCancel: true);
            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            ProgressDialogViewModel? progressDialog = null;
            IsLoadingRequest?.Invoke(this, true); // Set loading before showing dialog
            var deletionResults = new List<string>();
            bool cancelled = false;
            bool anySuccess = false;

            // Using statement for CancellationTokenSource
            using var cts = new CancellationTokenSource();
            // Link the external token if provided
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            var operationToken = linkedCts.Token; // Use this for the deletion loop

            try
            {
                 RunOnUIThread(() =>
                 {
                    progressDialog = _dialogService.ShowProgressDialog(
                       "Deleting Mods",
                       $"Preparing to delete {deletableMods.Count} mods...",
                       canCancel: true); 
                 });

                 // Allow dialog to appear
                 await Task.Delay(100, CancellationToken.None); 

                 operationToken.ThrowIfCancellationRequested(); 

                for (int i = 0; i < deletableMods.Count; i++)
                {
                    operationToken.ThrowIfCancellationRequested(); 

                    var mod = deletableMods[i];
                    var modIdentifier = $"{mod.Name} ({mod.PackageId})";
                    RunOnUIThread(() =>
                    {
                       if (progressDialog != null)
                       {
                           progressDialog.Message = $"Deleting {modIdentifier} ({i + 1}/{deletableMods.Count})...";
                           progressDialog.UpdateProgress((int)((double)(i + 1) / deletableMods.Count * 100.0));
                       }
                    });

try
                    {
                        if (Directory.Exists(mod.Path))
                        {
                            Debug.WriteLine($"[DeleteMultipleModsAsyncInternal] Attempting to delete: {mod.Path}");
                            await _deletionService.DeleteDirectoryRobustAsync(mod.Path, operationToken);
                            deletionResults.Add($"Successfully deleted {modIdentifier}");
                            anySuccess = true;
                            Debug.WriteLine($"[DeleteMultipleModsAsyncInternal] Successfully deleted: {mod.Path}");
                        }
                        else
                        {
                            deletionResults.Add($"Skipped {modIdentifier} (Path not found: {mod.Path})");
                             Debug.WriteLine($"[DeleteMultipleModsAsyncInternal] Skipped, path not found: {mod.Path}");
                        }
                    }
                    catch (OperationCanceledException) { throw; } 
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DeleteMultipleModsAsyncInternal] Failed to delete {modIdentifier}: {ex}");
                        deletionResults.Add($"Failed to delete {modIdentifier}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                deletionResults.Add("Operation cancelled by user.");
                Debug.WriteLine("[DeleteMultipleModsAsyncInternal] Deletion loop cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeleteMultipleModsAsyncInternal] Unexpected error: {ex}");
                cancelled = true; 
                deletionResults.Add($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                 // Close progress dialog on UI thread
                 RunOnUIThread(() => progressDialog?.CompleteOperation(cancelled ? "Deletion Cancelled" : "Deletion Completed"));

                IsLoadingRequest?.Invoke(this, false);

                 // Build and show summary report on UI thread
                 await RunOnUIThreadAsync(async () =>
                 {
                     var summaryMessage = new StringBuilder();
                     if (nonDeletableCount > 0) summaryMessage.AppendLine($"{nonDeletableCount} mod(s) were skipped (Core/DLC or missing info).\n");
                     summaryMessage.AppendLine(string.Join("\n", deletionResults));
                     await _dialogService.ShowInformation("Deletion Summary", summaryMessage.ToString());
                 });

                 if (anySuccess && !cancelled)
                 {
                     var actuallyDeletedMods = deletableMods.Where(m => !deletionResults.Any(r => r.Contains("Failed") && r.Contains(m.Name))).ToList();
                     await HandlePostDeletionAsync(actuallyDeletedMods);
                 }
            }
        }

private async Task HandlePostDeletionAsync(IEnumerable<ModItem> mods, bool suppressWarnings = false)
        {
            if (mods == null || !mods.Any()) return;

            var result = _modListManager.RemoveMods(mods);

if (result.ActivePackageIdLost)
            {
                Debug.WriteLine("[HandlePostDeletionAsync] Active mod(s) deleted and PackageID lost. Saving updated active list.");
                await ExecuteSaveMods(suppressWarnings);
            }
            else if (result.InstanceRemoved)
            {
                 Debug.WriteLine("[HandlePostDeletionAsync] Active mod instance deleted but duplicate took over. No save needed for Game, but UI updated.");

}
        }

        private async Task DeleteDuplicateModsAsyncInternal(List<string>? pathsToDelete)
        {
            if (pathsToDelete == null || !pathsToDelete.Any()) return;

            IsLoadingRequest?.Invoke(this, true); // Set loading state early

            var successfullyDeletedCount = 0;
            var errorMessages = new List<string>();
            bool refreshNeeded = false;
            ProgressDialogViewModel? progressDialog = null;

            // Prepare lookup
            var allModsLookup = _modListManager.GetAllMods()
                                 .Where(m => !string.IsNullOrEmpty(m.Path))
                                 .ToDictionary(m => m.Path, m => m, StringComparer.OrdinalIgnoreCase);

            using var cts = new CancellationTokenSource();
            var ct = cts.Token; // Token for cancellation

            try
            {
                 RunOnUIThread(() =>
                 {
                     progressDialog = _dialogService.ShowProgressDialog(
                        "Deleting Duplicates",
                        "Starting deletion...",
                        canCancel: false, 
                        isIndeterminate: false);

                 });
                 await Task.Delay(100, CancellationToken.None); // Allow UI to update
                 ct.ThrowIfCancellationRequested(); // Check initial cancellation

                for (int i = 0; i < pathsToDelete.Count; i++)
                {
                    ct.ThrowIfCancellationRequested(); // Check cancellation before each item

                    var path = pathsToDelete[i];
                    if (string.IsNullOrEmpty(path)) continue;

                    allModsLookup.TryGetValue(path, out ModItem? modInfo);
                    string modIdentifier = modInfo != null ? $"{modInfo.Name} ({modInfo.PackageId})" : $"'{path}'";
                     RunOnUIThread(() =>
                     {
                       if (progressDialog != null)
                       {
                           progressDialog.Message = $"Deleting {modIdentifier} ({i + 1}/{pathsToDelete.Count})...";
                           progressDialog.UpdateProgress((int)(((double)i + 1) / pathsToDelete.Count * 100.0));
                       }
                     });

try
                    {
                        if (Directory.Exists(path))
                        {
                            Debug.WriteLine($"[DeleteDuplicateModsAsyncInternal] Deleting: {path}");
                            await _deletionService.DeleteDirectoryRobustAsync(path, ct);
                            successfullyDeletedCount++;
                            refreshNeeded = true;
                            Debug.WriteLine($"[DeleteDuplicateModsAsyncInternal] Deleted: {path}");
                        }
                        else
                        {
                            Debug.WriteLine($"[DeleteDuplicateModsAsyncInternal] Path not found, skipping: {path}");
                            errorMessages.Add($"Path not found for {modIdentifier}");
                        }
                    }
                    catch (OperationCanceledException) { throw; } // Re-throw cancellation
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DeleteDuplicateModsAsyncInternal] Error deleting {modIdentifier}: {ex}");
                        errorMessages.Add($"Error deleting {modIdentifier}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[DeleteDuplicateModsAsyncInternal] Deletion cancelled.");
                errorMessages.Add("Deletion process was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeleteDuplicateModsAsyncInternal] Unexpected error during batch deletion: {ex}");
                errorMessages.Add($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                 await RunOnUIThreadAsync(async () =>
                 {
                    // Complete/close dialog first
                    bool wasCancelled = errorMessages.Any(m => m.Contains("cancelled"));
                    progressDialog?.CompleteOperation(wasCancelled ? "Deletion Cancelled" : "Deletion Completed");

                    IsLoadingRequest?.Invoke(this, false);

                    // Show report third
                    var reportTitle = errorMessages.Any() ? "Deletion Complete with Issues" : "Deletion Complete";
                    var reportMessage = new StringBuilder();
                    if (successfullyDeletedCount > 0)
                        reportMessage.AppendLine($"Successfully deleted {successfullyDeletedCount} duplicate mod folder(s).");
                    if (errorMessages.Any())
                    {
                        if (reportMessage.Length > 0) reportMessage.AppendLine();
                        reportMessage.AppendLine("Issues encountered:");
                        reportMessage.Append(string.Join("\n - ", errorMessages.Prepend(""))); // Prepend for bullet points
                    }
                    if (reportMessage.Length == 0)
                        reportMessage.AppendLine("No duplicate mods were deleted.");

                    await _dialogService.ShowMessageWithCopy(reportTitle, reportMessage.ToString().Trim(),
                        errorMessages.Any() ? MessageDialogType.Warning : MessageDialogType.Information);

                    // Request refresh last (if needed and not cancelled)
                    if (refreshNeeded && !wasCancelled)
                    {
                        var allMods = _modListManager.GetAllMods().ToList();
                        var deletedMods = allMods.Where(m => pathsToDelete.Contains(m.Path, StringComparer.OrdinalIgnoreCase)).ToList();
                        await HandlePostDeletionAsync(deletedMods);
                    }
                 });
            }
        }
    }
}


