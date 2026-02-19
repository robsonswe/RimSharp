using RimSharp.AppDir.AppFiles; // For ViewModelBase/RunOnUIThread
using RimSharp.AppDir.Dialogs; // For ProgressDialogViewModel etc.
using RimSharp.Shared.Models;
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
        // Determines if a mod is eligible for deletion
        private bool CanBeDeleted(ModItem mod) =>
            mod != null &&
            mod.ModType != ModType.Core &&
            mod.ModType != ModType.Expansion &&
            !string.IsNullOrEmpty(mod.Path); // Path existence check is done just before deletion

        // --- Internal Deletion Logic ---

        // Helper for deleting a single mod (called by DeleteModCommand)
        private async Task DeleteSingleModAsyncInternal(ModItem mod, CancellationToken ct = default)
        {
            mod = mod ?? SelectedMod; // Ensure we have the mod item

            // Re-check CanExecute conditions specifically for this action
            if (!CanBeDeleted(mod))
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Precondition failed for mod '{mod?.Name}'.");
                RunOnUIThread(() => _dialogService.ShowWarning("Deletion Blocked", $"Cannot delete mod '{mod?.Name}'. It might be Core/DLC or essential information is missing."));
                return;
            }
             if (!Directory.Exists(mod.Path))
            {
                 Debug.WriteLine($"[DeleteSingleModAsyncInternal] Path not found for mod '{mod?.Name}': {mod.Path}");
                 RunOnUIThread(() => _dialogService.ShowWarning("Deletion Skipped", $"The folder for mod '{mod?.Name}' does not exist or was already deleted.\nPath: {mod.Path}"));
                 RequestDataRefresh?.Invoke(this, EventArgs.Empty); // Refresh in case it was deleted externally
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
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Attempting to delete directory: {mod.Path}");
                await _deletionService.DeleteDirectoryRobustAsync(mod.Path, ct);
                deletionSuccess = true;
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Successfully deleted: {mod.Path}");
                RunOnUIThread(() => _dialogService.ShowInformation("Deletion Successful", $"Mod '{mod.Name}' was deleted."));
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Deletion cancelled for '{mod.Path}'.");
                RunOnUIThread(() => _dialogService.ShowWarning("Deletion Cancelled", $"Deletion of mod '{mod.Name}' was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeleteSingleModAsyncInternal] Error deleting '{mod.Path}': {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Deletion Error", $"Could not delete mod '{mod.Name}'.\nError: {ex.Message}\nPath: {mod.Path}"));
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
                if (deletionSuccess)
                {
                    RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Helper for deleting multiple mods (called by DeleteModsCommand)
        private async Task DeleteMultipleModsAsyncInternal(IList selectedItems, CancellationToken ct = default)
        {
            selectedItems = selectedItems ?? SelectedItems;
            var mods = selectedItems?.Cast<ModItem>().ToList();

            if (mods == null || !mods.Any())
            {
                RunOnUIThread(() => _dialogService.ShowWarning("Deletion Blocked", "No mods selected for deletion."));
                return;
            }

            var deletableMods = mods.Where(CanBeDeleted).ToList();
            var nonDeletableCount = mods.Count - deletableMods.Count;

            if (deletableMods.Count == 0)
            {
                RunOnUIThread(() => _dialogService.ShowWarning("Deletion Blocked", "None of the selected mods can be deleted (Core/DLC or missing essential info)."));
                return;
            }

            var confirmationMessage = $"Are you sure you want to permanently delete {deletableMods.Count} mod(s)?";
            if (nonDeletableCount > 0)
            {
                 confirmationMessage += $"\n({nonDeletableCount} selected mod(s) like Core/DLC cannot be deleted and will be skipped).";
            }
            confirmationMessage += "\n\nThis action cannot be undone.";

            var result = _dialogService.ShowConfirmation("Confirm Deletion", confirmationMessage, showCancel: true);
            if (result != MessageDialogResult.OK) return;

            ProgressDialogViewModel progressDialog = null;
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
                 // Show progress dialog
                 // Make sure the DialogService call is thread-safe or run on UI thread if needed
                 RunOnUIThread(() =>
                 {
                    progressDialog = _dialogService.ShowProgressDialog(
                       "Deleting Mods",
                       $"Preparing to delete {deletableMods.Count} mods...",
                       canCancel: true); // Assuming the dialog returns a VM with a CancellationToken

                    // If the dialog provides its own cancellation, link it
                    // For now, we assume the dialog calls cts.Cancel() if its cancel button is pressed.
                    // This requires the dialog implementation to have access to the 'cts' or a way to signal cancellation.
                    // Example: progressDialog.CancelRequested += (_, _) => cts.Cancel();
                 });

                 // Allow dialog to appear
                 await Task.Delay(100, CancellationToken.None); // Small delay, use CancellationToken.None for UI delay

                 operationToken.ThrowIfCancellationRequested(); // Check if cancelled immediately after dialog shown

                for (int i = 0; i < deletableMods.Count; i++)
                {
                    operationToken.ThrowIfCancellationRequested(); // Check before each mod

                    var mod = deletableMods[i];
                    var modIdentifier = $"{mod.Name} ({mod.PackageId ?? "No ID"})";

                    // Update progress on UI thread
                    RunOnUIThread(() =>
                    {
                       progressDialog.Message = $"Deleting {modIdentifier} ({i + 1}/{deletableMods.Count})...";
                       progressDialog.UpdateProgress((int)((double)(i + 1) / deletableMods.Count * 100.0));
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
                    catch (OperationCanceledException) { throw; } // Re-throw cancellation to be caught outside loop
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
            catch (Exception ex) // Catch unexpected errors during the process setup
            {
                Debug.WriteLine($"[DeleteMultipleModsAsyncInternal] Unexpected error: {ex}");
                cancelled = true; // Treat unexpected errors like cancellation for reporting
                deletionResults.Add($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                 // Close progress dialog on UI thread
                 RunOnUIThread(() => progressDialog?.CompleteOperation(cancelled ? "Deletion Cancelled" : "Deletion Completed"));

                // Set loading false AFTER dialog is handled
                IsLoadingRequest?.Invoke(this, false);

                 // Build and show summary report on UI thread
                 RunOnUIThread(() =>
                 {
                     var summaryMessage = new StringBuilder();
                     if (nonDeletableCount > 0) summaryMessage.AppendLine($"{nonDeletableCount} mod(s) were skipped (Core/DLC or missing info).\n");
                     summaryMessage.AppendLine(string.Join("\n", deletionResults));
                     _dialogService.ShowInformation("Deletion Summary", summaryMessage.ToString());
                 });

                 // Request refresh only if successful deletions occurred and not cancelled
                 if (anySuccess && !cancelled)
                 {
                     RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                 }
            }
        }


        // Helper for deleting duplicate mods (called by DuplicateModDialogViewModel callback)
        // async void is acceptable here as it's effectively a top-level event handler from the dialog interaction
        private async void DeleteDuplicateModsAsyncInternal(List<string> pathsToDelete)
        {
            if (pathsToDelete == null || !pathsToDelete.Any()) return;

            IsLoadingRequest?.Invoke(this, true); // Set loading state early

            var successfullyDeletedCount = 0;
            var errorMessages = new List<string>();
            bool refreshNeeded = false;
            ProgressDialogViewModel progressDialog = null;

            // Prepare lookup
            var allModsLookup = _modListManager.GetAllMods()
                                 .Where(m => !string.IsNullOrEmpty(m.Path))
                                 .ToDictionary(m => m.Path, m => m, StringComparer.OrdinalIgnoreCase);

            using var cts = new CancellationTokenSource();
            var ct = cts.Token; // Token for cancellation

            try
            {
                 // Show progress dialog on UI thread
                 RunOnUIThread(() =>
                 {
                     progressDialog = _dialogService.ShowProgressDialog(
                        "Deleting Duplicates",
                        "Starting deletion...",
                        canCancel: false, // Or true if cancellation is implemented via cts
                        isIndeterminate: false);
                    // Link dialog cancellation if needed: progressDialog.CancelRequested += (_,_) => cts.Cancel();
                 });
                 await Task.Delay(100, CancellationToken.None); // Allow UI to update
                 ct.ThrowIfCancellationRequested(); // Check initial cancellation

                for (int i = 0; i < pathsToDelete.Count; i++)
                {
                    ct.ThrowIfCancellationRequested(); // Check cancellation before each item

                    var path = pathsToDelete[i];
                    if (string.IsNullOrEmpty(path)) continue;

                    allModsLookup.TryGetValue(path, out ModItem modInfo);
                    string modIdentifier = modInfo != null ? $"{modInfo.Name} ({modInfo.PackageId ?? "No ID"})" : $"'{path}'";

                    // Update progress on UI thread
                     RunOnUIThread(() =>
                     {
                       progressDialog.Message = $"Deleting {modIdentifier} ({i + 1}/{pathsToDelete.Count})...";
                       progressDialog.UpdateProgress((int)(((double)i + 1) / pathsToDelete.Count * 100.0));
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
                 // Ensure final state update happens on UI thread
                 RunOnUIThread(() =>
                 {
                    // Complete/close dialog first
                    bool wasCancelled = errorMessages.Any(m => m.Contains("cancelled"));
                    progressDialog?.CompleteOperation(wasCancelled ? "Deletion Cancelled" : "Deletion Completed");

                    // Set loading state false second
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

                    _dialogService.ShowMessageWithCopy(reportTitle, reportMessage.ToString().Trim(),
                        errorMessages.Any() ? MessageDialogType.Warning : MessageDialogType.Information);

                    // Request refresh last (if needed and not cancelled)
                    if (refreshNeeded && !wasCancelled)
                    {
                        RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                    }
                 });
            }
        }
    }
}