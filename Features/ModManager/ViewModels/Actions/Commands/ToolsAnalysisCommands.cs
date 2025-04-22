#nullable enable
using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    public partial class ModActionsViewModel
    {
        private void InitializeToolsAnalysisCommands()
        {
            ResolveDependenciesCommand = CreateCancellableAsyncCommand(
                ExecuteResolveDependencies,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            CheckIncompatibilitiesCommand = CreateCancellableAsyncCommand(
                ExecuteCheckIncompatibilities,
                CanExecuteCheckIncompatibilities,
                observedProperties: new[] { nameof(IsParentLoading) }); // Primarily loading state + list content

            CheckDuplicatesCommand = CreateCommand(
                ExecuteCheckDuplicates,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });


            RunGameCommand = CreateCommand(
                ExecuteRunGame,
                CanExecuteRunGame, // Uses HasValidPaths
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) }); // Already correctly observes both
        }


        private void InitializePlaceholderCommands()
        {
            StripModsCommand = CreateCommand(
                () => _dialogService.ShowInformation("Not Implemented", "Strip mods: Functionality not yet implemented."),
                () => true,
                nameof(IsParentLoading));

            FixIntegrityCommand = CreateCommand(
                () => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."),
                () => true,
                nameof(IsParentLoading));

            RunGameCommand = CreateCommand(
                ExecuteRunGame,
                CanExecuteRunGame,
                nameof(IsParentLoading), nameof(HasValidPaths));
        }
        private bool CanExecuteCheckIncompatibilities() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();

        private async Task ExecuteResolveDependencies(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null; // Now used for queueing phase
            CancellationTokenSource? linkedCts = null;

            try
            {
                // 1. Perform the core dependency resolution (finds missing, activates existing)
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                ct.ThrowIfCancellationRequested();

                var (addedMods, missingDependencies) = result;

                // 2. Handle Results - Decide which dialog to show (if any)
                if (missingDependencies.Count > 0)
                {
                    // --- Show the NEW Interactive Dialog ---
                    DependencyResolutionDialogResult dialogResult = DependencyResolutionDialogResult.Cancel;
                    List<string>? selectedSteamIds = null; // Make nullable

                    await RunOnUIThreadAsync(() =>
                    {
                        var viewModel = new DependencyResolutionDialogViewModel(missingDependencies);
                        dialogResult = _dialogService.ShowDependencyResolutionDialog(viewModel);
                        if (dialogResult == DependencyResolutionDialogResult.Download)
                        {
                            selectedSteamIds = viewModel.GetSelectedSteamIds();
                        }
                    });

                    ct.ThrowIfCancellationRequested(); // Check after dialog interaction

                    // --- Process Download Request using the new service ---
                    if (dialogResult == DependencyResolutionDialogResult.Download && selectedSteamIds != null && selectedSteamIds.Any())
                    {
                        QueueProcessResult queueResult = new QueueProcessResult();
                        try
                        {
                            // Show progress dialog for the download/API check phase
                            await RunOnUIThreadAsync(() =>
                            {
                                progressDialog = _dialogService.ShowProgressDialog(
                               "Verifying & Queueing Dependencies",
                               "Starting...",
                               canCancel: true,
                               isIndeterminate: false,
                               cts: null, // Linked CTS created below
                               closeable: true);
                            });
                            if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                            // Link cancellation tokens
                            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                            var combinedToken = linkedCts.Token;

                            // Setup Progress Reporter
                            var progressReporter = new Progress<QueueProcessProgress>(update =>
                            {
                                RunOnUIThread(() => // Ensure UI thread for updates
                            {
                                if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                                {
                                    progressDialog.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                                    progressDialog.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                                }
                            });
                            });

                            // Call the central service
                            queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(selectedSteamIds, progressReporter, combinedToken);

                            // --- Show Summary ---
                            await RunOnUIThreadAsync(() =>
                            {
                                if (queueResult.WasCancelled)
                                {
                                    Debug.WriteLine("Queueing dependencies was cancelled.", nameof(ModActionsViewModel));
                                    progressDialog?.ForceClose();
                                    _dialogService.ShowWarning("Operation Cancelled", "Queueing missing dependencies was cancelled.");
                                    return; // Exit early on cancellation
                                }

                                progressDialog?.CompleteOperation("Dependency queueing complete.");

                                var sb = new StringBuilder();
                                if (queueResult.SuccessfullyAdded > 0) sb.AppendLine($"{queueResult.SuccessfullyAdded} missing dependency mod(s) added to the download queue.");
                                else sb.AppendLine("No new dependency mods were added to the download queue.");

                                if (queueResult.AlreadyQueued > 0) sb.AppendLine($"{queueResult.AlreadyQueued} selected dependency mod(s) were already in the queue.");
                                if (queueResult.FailedProcessing > 0)
                                {
                                    sb.AppendLine($"{queueResult.FailedProcessing} selected dependency mod(s) could not be added due to errors or invalid Steam info:");
                                    foreach (var errMsg in queueResult.ErrorMessages.Take(5)) sb.AppendLine($"  - {errMsg}");
                                    if (queueResult.ErrorMessages.Count > 5) sb.AppendLine("    (Check logs for more details...)");
                                }

                                // Include locally activated mods in the summary
                                if (addedMods.Count > 0)
                                {
                                    sb.AppendLine();
                                    sb.AppendLine($"{addedMods.Count} inactive dependency mod(s) were automatically activated:");
                                    foreach (var mod in addedMods.Take(5)) sb.AppendLine($"- {mod.Name}");
                                    if (addedMods.Count > 5) sb.AppendLine("  ...");
                                }

                                _dialogService.ShowInformation("Dependencies Processed", sb.ToString().Trim());

                                if (queueResult.SuccessfullyAdded > 0)
                                {
                                    _navigationService.RequestTabSwitch("Downloader");
                                }
                            });
                        }
                        catch (OperationCanceledException) // Catch cancellation during queue processing
                        {
                            // Handled by the WasCancelled check in the UI thread block
                            Debug.WriteLine("[ExecuteResolveDependencies] Dependency queueing cancelled (caught inner).", nameof(ModActionsViewModel));
                            await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                        }
                        finally // Ensure dialog closure and CTS disposal for queue phase
                        {
                            await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                            linkedCts?.Dispose();
                            linkedCts = null;
                            progressDialog = null;
                        }
                    }
                    else if (addedMods.Count > 0) // Missing deps exist, but user cancelled download OR no downloadable ones selected
                    {
                        // Still inform about locally activated mods if any (same as before)
                        await RunOnUIThreadAsync(() =>
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine("Some required dependencies were missing or not selected for download.");
                            sb.AppendLine();
                            sb.AppendLine($"{addedMods.Count} inactive dependency mod(s) were automatically activated:");
                            foreach (var mod in addedMods.Take(5)) sb.AppendLine($"- {mod.Name}");
                            if (addedMods.Count > 5) sb.AppendLine("  ...");
                            _dialogService.ShowInformation("Dependencies Resolved (Partial)", sb.ToString().Trim());
                        });
                    }
                    // else: Missing dependencies exist, but none were selected and none were activated locally. No extra message needed.

                }
                else if (addedMods.Count > 0) // Only local activations occurred
                {
                    // (Same logic as before)
                    await RunOnUIThreadAsync(() =>
                    {
                        var message = new StringBuilder();
                        message.AppendLine("Dependencies automatically activated:");
                        message.AppendLine();
                        foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                        _dialogService.ShowInformation("Dependencies Activated", message.ToString().TrimEnd());
                    });
                }
                else // No missing dependencies and no local activations
                {
                    // (Same logic as before)
                    await RunOnUIThreadAsync(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found and no new dependencies were activated.");
                    });
                }
            }
            catch (OperationCanceledException) // Catch cancellation during initial ModListManager.ResolveDependencies or dialog interaction
            {
                Debug.WriteLine("[ExecuteResolveDependencies] Operation cancelled (outer).");
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose()); // Close progress if open
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Dependency resolution was cancelled."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving dependencies: {ex}");
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose()); // Close progress on error too
                RunOnUIThread(() => _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}"));
            }
            finally
            {
                // Final cleanup (ensure progress dialog is closed, CTS disposed if created, loading state reset)
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                linkedCts?.Dispose(); // Dispose if created
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        private async Task ExecuteCheckIncompatibilities(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                ct.ThrowIfCancellationRequested();
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods), ct);
                ct.ThrowIfCancellationRequested();

                if (incompatibilities.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found."));
                    return;
                }

                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities), ct);
                ct.ThrowIfCancellationRequested();

                if (groups.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Compatibility Check", "Incompatibilities found but could not be grouped for resolution."));
                    return;
                }

                await RunOnUIThreadAsync(() => ShowIncompatibilityDialog(groups));
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

        private void ExecuteCheckDuplicates()
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
                    RunOnUIThread(() => ShowDuplicateModsDialog(actualDuplicateGroups));
                }
                else
                {
                    RunOnUIThread(() => _dialogService.ShowInformation("Duplicates Check", "No duplicate mods found based on Package ID."));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking duplicates: {ex}");
                RunOnUIThread(() => _dialogService.ShowError("Duplicates Error", $"Error checking for duplicate mods: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(
                groups,
                ApplyIncompatibilityResolutions,
                () => { }
            );
            var dialog = new ModIncompatibilityDialogView(dialogViewModel);
            dialog.Owner = Application.Current?.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove)
        {
            if (modsToRemove == null || modsToRemove.Count == 0) return;
            try
            {
                _modListManager.DeactivateMods(modsToRemove);
                _dialogService.ShowInformation("Incompatibilities Resolved", $"Resolved incompatibilities by deactivating {modsToRemove.Count} mod(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying incompatibility resolutions: {ex}");
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }

        private void ShowDuplicateModsDialog(List<IGrouping<string, ModItem>> duplicateGroups)
        {
            var dialogViewModel = new DuplicateModDialogViewModel(
                duplicateGroups,
                pathsToDelete => DeleteDuplicateModsAsyncInternal(pathsToDelete),
                () => { });
            var view = new DuplicateModDialogView(dialogViewModel);
            view.Owner = Application.Current?.MainWindow;
            view.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            view.ShowDialog();
        }

        private async Task ExecuteCheckReplacements(CancellationToken ct)
        {
            if (!HasValidPaths) return;

            IsLoadingRequest?.Invoke(this, true);

            ProgressDialogViewModel? progressViewModel = null; // Used for both phases now
            CancellationTokenSource? linkedCts = null;
            List<(ModItem Original, ModReplacementInfo Replacement)> modsWithValidReplacements = new List<(ModItem, ModReplacementInfo)>();

            try
            {
                // --- Phase 1: Find potential replacements (remains mostly the same) ---
                await Task.Run(() => _modService.LoadMods(), ct);
                ct.ThrowIfCancellationRequested();
                var loadedMods = _modService.GetLoadedMods().ToList();
                ct.ThrowIfCancellationRequested();

                if (loadedMods == null || !loadedMods.Any())
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No mods loaded to check for replacements."));
                    return;
                }

                // Show Indeterminate Progress Dialog for search phase
                await RunOnUIThreadAsync(() =>
                {
                    // Create CTS for this phase, linked to the command's token
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    progressViewModel = _dialogService.ShowProgressDialog(
                    "Checking Replacements",
                    "Searching for mod replacements...",
                    canCancel: true,
                    isIndeterminate: true,
                    cts: linkedCts, // Use the new CTS
                    closeable: true);
                });
                if (progressViewModel == null) throw new InvalidOperationException("Progress dialog view model was not created.");
                if (linkedCts == null) throw new InvalidOperationException("Linked CTS not created.");

                var combinedTokenPhase1 = linkedCts.Token;

                // Perform the search and validation operation
                await Task.Run(() =>
                {
                    foreach (var mod in loadedMods)
                    {
                        combinedTokenPhase1.ThrowIfCancellationRequested();
                        // ... (rest of the replacement finding and version check logic remains the same) ...
                        ModReplacementInfo? replacement = null;
                        if (!string.IsNullOrEmpty(mod.SteamId))
                        {
                            replacement = _replacementService.GetReplacementBySteamId(mod.SteamId);
                        }

                        if (replacement != null)
                        {
                            var originalSupportedVersions = mod.SupportedVersionStrings?
                           .Select(v => v.Trim())
                           .Where(v => !string.IsNullOrEmpty(v))
                           .ToList() ?? new List<string>();
                            var replacementSupportedVersions = replacement.ReplacementVersionList ?? new List<string>();

                            bool versionsAreValid;
                            if (!originalSupportedVersions.Any()) versionsAreValid = true;
                            else if (!replacementSupportedVersions.Any()) versionsAreValid = false;
                            else
                            {
                                var replacementVersionSet = new HashSet<string>(replacementSupportedVersions, StringComparer.OrdinalIgnoreCase);
                                versionsAreValid = originalSupportedVersions.All(ov => replacementVersionSet.Contains(ov));
                            }

                            if (versionsAreValid)
                            {
                                modsWithValidReplacements.Add((mod, replacement));
                            }
                            else
                            {
                                Debug.WriteLine($"[ExecuteCheckReplacements] Discarding replacement rule for '{mod.Name}' ({mod.SteamId}) due to version mismatch...");
                            }
                        }
                    }
                }, combinedTokenPhase1);

                // Close progress dialog for search phase cleanly
                await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Check complete."));
                progressViewModel = null; // Reset
                linkedCts?.Dispose(); // Dispose CTS for phase 1
                linkedCts = null;

                ct.ThrowIfCancellationRequested(); // Check cancellation between phases

                // --- Phase 2: User Selection and Queueing ---
                if (modsWithValidReplacements.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No suitable replacement mods found for your current mod list (considering version compatibility)."));
                    return;
                }

                ModReplacementDialogResult dialogResult = ModReplacementDialogResult.Cancel;
                List<string>? selectedReplacementSteamIds = null; // Store only IDs

                await RunOnUIThreadAsync(() =>
                {
                    var viewModel = new ModReplacementDialogViewModel(modsWithValidReplacements, loadedMods);
                    dialogResult = _dialogService.ShowModReplacementDialog(viewModel);
                    if (dialogResult == ModReplacementDialogResult.Download)
                    {
                        // Get only the Steam IDs of the selected replacements
                        selectedReplacementSteamIds = viewModel.GetSelectedReplacements()
                            .Select(r => r.ReplacementInfo?.ReplacementSteamId)
                            .Where(id => !string.IsNullOrEmpty(id))
                            .Select(id => id!) // Non-null assertion after Where clause
                            .ToList();
                    }
                });

                ct.ThrowIfCancellationRequested(); // Check cancellation after dialog

                // --- Queue using the new service ---
                if (dialogResult == ModReplacementDialogResult.Download && selectedReplacementSteamIds != null && selectedReplacementSteamIds.Any())
                {
                    QueueProcessResult queueResult = new QueueProcessResult();
                    try
                    {
                        // Show progress dialog for queueing phase
                        await RunOnUIThreadAsync(() =>
                        {
                            // Create new CTS for this phase, linked to command's token
                            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            progressViewModel = _dialogService.ShowProgressDialog(
                              "Verifying & Queueing Replacements",
                              "Starting...",
                              canCancel: true,
                              isIndeterminate: false,
                              cts: linkedCts, // Use the new CTS
                              closeable: true);
                        });
                        if (progressViewModel == null) throw new InvalidOperationException("Progress dialog view model was not created.");
                        if (linkedCts == null) throw new InvalidOperationException("Linked CTS not created.");

                        var combinedTokenPhase2 = linkedCts.Token;

                        // Setup Progress Reporter
                        var progressReporter = new Progress<QueueProcessProgress>(update =>
                        {
                            RunOnUIThread(() => // Ensure UI thread
                          {
                                 if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                                 {
                                     progressViewModel.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                                     progressViewModel.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                                 }
                             });
                        });

                        // Call the central service
                        queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(selectedReplacementSteamIds, progressReporter, combinedTokenPhase2);

                        // Show summary message on UI Thread
                        await RunOnUIThreadAsync(() =>
                        {
                            if (queueResult.WasCancelled)
                            {
                                Debug.WriteLine("Queueing replacements was cancelled.", nameof(ModActionsViewModel));
                                progressViewModel?.ForceClose();
                                _dialogService.ShowWarning("Operation Cancelled", "Queueing selected replacement mods was cancelled.");
                                return;
                            }

                            progressViewModel?.CompleteOperation("Replacements queueing complete.");

                            var sb = new StringBuilder();
                            if (queueResult.SuccessfullyAdded > 0) sb.AppendLine($"{queueResult.SuccessfullyAdded} replacement mod(s) added to the download queue.");
                            else sb.AppendLine("No new replacement mods were added to the download queue.");
                            if (queueResult.AlreadyQueued > 0) sb.AppendLine($"{queueResult.AlreadyQueued} selected replacement mod(s) were already in the queue.");
                            if (queueResult.FailedProcessing > 0)
                            {
                                sb.AppendLine($"{queueResult.FailedProcessing} selected replacement mod(s) could not be added due to errors:");
                                foreach (var errMsg in queueResult.ErrorMessages.Take(5)) sb.AppendLine($"  - {errMsg}");
                                if (queueResult.ErrorMessages.Count > 5) sb.AppendLine("    (Check logs for more details...)");
                            }

                            _dialogService.ShowInformation("Replacements Processed", sb.ToString().Trim());
                            if (queueResult.SuccessfullyAdded > 0)
                            {
                                _navigationService.RequestTabSwitch("Downloader");
                            }
                        });
                    }
                    catch (OperationCanceledException) // Catch cancellation during queue processing
                    {
                        Debug.WriteLine("[ExecuteCheckReplacements] Replacement queueing cancelled (caught inner).", nameof(ModActionsViewModel));
                        await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                    }
                    finally // Ensure dialog closure and CTS disposal for queue phase
                    {
                        await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                        linkedCts?.Dispose(); // Dispose phase 2 CTS
                        linkedCts = null;
                        progressViewModel = null;
                    }
                } // end if Download selected
            } // end outer try
            catch (OperationCanceledException) // Catch cancellation during phase 1 or dialog interaction
            {
                Debug.WriteLine("[ExecuteCheckReplacements] Operation cancelled (outer).", nameof(ModActionsViewModel));
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose(); // Close whichever progress dialog might be open
                    _dialogService.ShowWarning("Operation Cancelled", "Checking for mod replacements was cancelled.");
                });
            }
            catch (Exception ex)
            {
                //_logger.LogException(ex, "Error checking replacements", nameof(ModActionsViewModel));
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose();
                    _dialogService.ShowError("Replacement Error", $"An error occurred while checking for replacements: {ex.Message}");
                });
            }
            finally
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose()); // Ensure final close
                linkedCts?.Dispose(); // Dispose if phase 1 CTS was left hanging due to early exit/error
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        private bool CanExecuteRunGame()
        {
            return !IsParentLoading && HasValidPaths;
        }

        private void ExecuteRunGame()
        {
            try
            {
                var gamePath = _pathService.GetGamePath();
                if (string.IsNullOrEmpty(gamePath))
                {
                    _dialogService.ShowError("Game Path Error", "Game path is not set.");
                    return;
                }

                string exePath = Path.Combine(gamePath, "RimWorldWin64.exe");
                if (!File.Exists(exePath))
                {
                    exePath = Path.Combine(gamePath, "RimWorld.exe");
                    if (!File.Exists(exePath))
                    {
                        _dialogService.ShowError("Game Not Found", "Could not find RimWorld executable in the specified game path.");
                        return;
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = gamePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Launch Error", $"Failed to launch RimWorld: {ex.Message}");
            }
        }

    }
}