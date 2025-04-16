using RimSharp.Core.Commands;
using RimSharp.Features.ModManager.Dialogs.Dependencies;
using RimSharp.Features.ModManager.Dialogs.DuplicateMods;
using RimSharp.Features.ModManager.Dialogs.Incompatibilities;
using RimSharp.Features.ModManager.Dialogs.Replacements;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.MyApp.AppFiles;
using RimSharp.MyApp.Dialogs;
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

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    public partial class ModActionsViewModel
    {
        private void InitializeToolsAnalysisCommands()
        {
            ResolveDependenciesCommand = CreateCancellableAsyncCommand(
                ExecuteResolveDependencies,
                CanExecuteSimpleCommands,
                nameof(IsParentLoading));

            CheckIncompatibilitiesCommand = CreateCancellableAsyncCommand(
                ExecuteCheckIncompatibilities,
                CanExecuteCheckIncompatibilities,
                nameof(IsParentLoading));

            CheckDuplicatesCommand = CreateCommand(
                ExecuteCheckDuplicates,
                CanExecuteSimpleCommands,
                nameof(IsParentLoading));
            CheckReplacementsCommand = CreateCancellableAsyncCommand(
                execute: ExecuteCheckReplacements,
                canExecute: CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

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
            ProgressDialogViewModel progressDialog = null; // For download phase
            CancellationTokenSource linkedCts = null; // For download phase cancellation

            try
            {
                // 1. Perform the core dependency resolution (finds missing, activates existing)
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                ct.ThrowIfCancellationRequested(); // Check after potentially long operation

                var (addedMods, missingDependencies) = result;

                // 2. Handle Results - Decide which dialog to show (if any)
                if (missingDependencies.Count > 0)
                {
                    // --- Show the NEW Interactive Dialog ---
                    DependencyResolutionDialogResult dialogResult = DependencyResolutionDialogResult.Cancel;
                    List<string> selectedSteamIds = null;

                    // Create and show the dialog on the UI thread
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

                    // --- Process Download Request ---
                    if (dialogResult == DependencyResolutionDialogResult.Download && selectedSteamIds != null && selectedSteamIds.Any())
                    {
                        int addedCount = 0;
                        int alreadyQueuedCount = 0;
                        int errorCount = 0;
                        var addedNames = new List<string>(); // Store names for summary

                        // Show progress dialog for the download/API check phase
                        await RunOnUIThreadAsync(() =>
                        {
                             // Use non-null cts for cancellation
                             progressDialog = _dialogService.ShowProgressDialog(
                                "Verifying Dependencies",
                                "Checking dependencies with Steam API...",
                                canCancel: true,
                                isIndeterminate: false, // Determinate progress
                                cts: null, // Create linked CTS below
                                closeable: true);
                         });

                        if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                        // Link cancellation tokens (main command token + dialog token)
                        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                        var combinedToken = linkedCts.Token;

                        try
                        {
                            for (int i = 0; i < selectedSteamIds.Count; i++)
                            {
                                combinedToken.ThrowIfCancellationRequested();
                                var steamId = selectedSteamIds[i];

                                // Update progress for the *second* dialog
                                string progressMessage = $"Checking dependency {steamId}... ({i + 1}/{selectedSteamIds.Count})";
                                await RunOnUIThreadAsync(() =>
                                {
                                    if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                                    {
                                        progressDialog.Message = progressMessage;
                                        progressDialog.Progress = (int)((double)(i + 1) / selectedSteamIds.Count * 100);
                                    }
                                });

                                // Check if already in queue
                                if (_downloadQueueService.IsInQueue(steamId))
                                {
                                    Debug.WriteLine($"[ExecuteResolveDependencies] Dependency mod '{steamId}' is already in the download queue.");
                                    alreadyQueuedCount++;
                                    continue;
                                }

                                // --- Steam API Check ---
                                try
                                {
                                    var apiResponse = await _steamApiClient.GetFileDetailsAsync(steamId, combinedToken);
                                    if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
                                    {
                                        Debug.WriteLine($"[ExecuteResolveDependencies] No details returned for dependency mod '{steamId}'");
                                        errorCount++;
                                        continue;
                                    }

                                    var details = apiResponse.Response.PublishedFileDetails.First();
                                    if (details.Result != 1) // 1 = OK according to Steam API docs
                                    {
                                        Debug.WriteLine($"[ExecuteResolveDependencies] API indicated failure retrieving details for dependency mod '{steamId}'. Result Code: {details.Result}");
                                        errorCount++;
                                        continue;
                                    }

                                    // --- Add to Queue ---
                                    DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
                                    DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;

                                    var modInfoDto = new ModInfoDto
                                    {
                                        Name = details.Title ?? $"Unknown Mod {steamId}", // Use title or fallback
                                        SteamId = steamId,
                                        Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamId}",
                                        PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                                        StandardDate = apiUpdateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                                        // Add other relevant info if available from details (Author, etc.)
                                    };

                                    if (_downloadQueueService.AddToQueue(modInfoDto))
                                    {
                                        addedCount++;
                                        addedNames.Add(modInfoDto.Name);
                                        Debug.WriteLine($"[ExecuteResolveDependencies] Added '{modInfoDto.Name}' ({modInfoDto.SteamId}) to download queue.");
                                    }
                                    else
                                    {
                                        // This might happen if AddToQueue has internal checks that fail
                                        Debug.WriteLine($"[ExecuteResolveDependencies] Failed to add '{modInfoDto.Name}' ({modInfoDto.SteamId}) to queue (returned false).");
                                        errorCount++;
                                    }
                                }
                                catch (OperationCanceledException) { throw; } // Re-throw cancellation
                                catch (Exception apiEx)
                                {
                                    Debug.WriteLine($"[ExecuteResolveDependencies] Error checking Steam API for dependency mod '{steamId}': {apiEx.Message}");
                                    errorCount++;
                                    // Continue with the next item
                                }
                            } // --- End For Loop ---

                            // --- Show Summary ---
                            await RunOnUIThreadAsync(() =>
                            {
                                progressDialog?.CompleteOperation("Dependency checks complete.");

                                var sb = new StringBuilder();
                                if (addedCount > 0) sb.AppendLine($"{addedCount} missing dependency mod(s) added to the download queue.");
                                else sb.AppendLine("No new dependency mods were added to the download queue.");

                                if (alreadyQueuedCount > 0) sb.AppendLine($"{alreadyQueuedCount} selected dependency mod(s) were already in the queue.");
                                if (errorCount > 0) sb.AppendLine($"{errorCount} selected dependency mod(s) could not be added due to errors or invalid Steam info.");

                                if (addedMods.Count > 0) // Also mention mods activated locally
                                {
                                     sb.AppendLine();
                                     sb.AppendLine($"{addedMods.Count} inactive dependency mod(s) were automatically activated:");
                                     foreach (var mod in addedMods.Take(5)) // Show first few
                                         sb.AppendLine($"- {mod.Name}");
                                     if(addedMods.Count > 5) sb.AppendLine("  ...");
                                }


                                _dialogService.ShowInformation("Dependencies Processed", sb.ToString().Trim());

                                if (addedCount > 0)
                                {
                                    _navigationService.RequestTabSwitch("Downloader");
                                }
                            });
                        }
                        finally
                        {
                             // Ensure progress dialog is closed and CTS is disposed
                             await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                             linkedCts?.Dispose();
                             linkedCts = null;
                             progressDialog = null;
                        }
                    }
                    else if (addedMods.Count > 0) // Missing deps exist, but user cancelled download OR no downloadable ones selected
                    {
                        // Still inform about locally activated mods if any
                         await RunOnUIThreadAsync(() =>
                         {
                              var sb = new StringBuilder();
                              sb.AppendLine("Some required dependencies were missing or not selected for download.");
                              sb.AppendLine();
                              sb.AppendLine($"{addedMods.Count} inactive dependency mod(s) were automatically activated:");
                              foreach (var mod in addedMods.Take(5)) // Show first few
                                  sb.AppendLine($"- {mod.Name}");
                              if(addedMods.Count > 5) sb.AppendLine("  ...");
                              _dialogService.ShowInformation("Dependencies Resolved (Partial)", sb.ToString().Trim());
                         });
                    }
                    // else: Missing dependencies exist, but none were selected and none were activated locally. No extra message needed.

                }
                else if (addedMods.Count > 0)
                {
                    // --- Only local activations occurred, no missing dependencies ---
                    await RunOnUIThreadAsync(() =>
                    {
                        var message = new StringBuilder();
                        message.AppendLine("Dependencies automatically activated:");
                        message.AppendLine();
                        foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                        _dialogService.ShowInformation("Dependencies Activated", message.ToString().TrimEnd());
                    });
                }
                else
                {
                    // --- No missing dependencies and no local activations ---
                    await RunOnUIThreadAsync(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found and no new dependencies were activated.");
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteResolveDependencies] Operation cancelled.");
                 // Ensure progress dialog is closed if cancellation happened during download phase
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
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
                // Ensure progress dialog is closed and CTS disposed in all exit paths
                 await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                 linkedCts?.Dispose();
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

            ProgressDialogViewModel progressViewModel = null;
            CancellationTokenSource linkedCts = null;
            List<(ModItem Original, ModReplacementInfo Replacement)> modsWithValidReplacements = new List<(ModItem, ModReplacementInfo)>();

            try
            {
                // Get loaded mods
                await Task.Run(() => _modService.LoadMods(), ct);
                ct.ThrowIfCancellationRequested();
                var loadedMods = _modService.GetLoadedMods().ToList();
                ct.ThrowIfCancellationRequested();

                if (loadedMods == null || !loadedMods.Any())
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No mods loaded to check for replacements."));
                    return;
                }

                // --- CHANGE 1: Show Indeterminate Progress Dialog ---
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Replacements",
                        "Searching for mod replacements...", // Initial message
                        canCancel: true,
                        isIndeterminate: true, // <-- Use indeterminate progress
                        cts: null,
                        closeable: true);
                });

                if (progressViewModel == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressViewModel.CancellationToken);
                var combinedToken = linkedCts.Token;

                // Perform the search and validation operation
                await Task.Run(() =>
                {
                    // int processedCount = 0; // Not needed for message/progress calculation anymore

                    foreach (var mod in loadedMods)
                    {
                        combinedToken.ThrowIfCancellationRequested();

                        ModReplacementInfo replacement = null;
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
                            var replacementSupportedVersions = replacement.ReplacementVersionList;

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
                                Debug.WriteLine($"[ExecuteCheckReplacements] Discarding outdated rule for '{mod.Name}' ({mod.SteamId}). " +
                                                $"Original versions: [{string.Join(", ", originalSupportedVersions)}]. " +
                                                $"Replacement versions: [{string.Join(", ", replacementSupportedVersions)}].");
                            }
                        }
                    }
                }, combinedToken);

                // Close progress dialog cleanly (still useful to signal completion of this phase)
                await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Check complete."));
                progressViewModel = null;

                // --- Rest of the method remains the same ---
                // (Showing results, handling selection, second progress dialog for API checks)

                if (modsWithValidReplacements.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No suitable replacement mods found for your current mod list (considering version compatibility)."));
                    return;
                }

                ModReplacementDialogResult dialogResult = ModReplacementDialogResult.Cancel;
                List<ReplacementSelectionViewModel> selectedReplacements = null;

                await RunOnUIThreadAsync(() =>
                {
                    var viewModel = new ModReplacementDialogViewModel(modsWithValidReplacements, loadedMods);
                    dialogResult = _dialogService.ShowModReplacementDialog(viewModel);
                    if (dialogResult == ModReplacementDialogResult.Download)
                    {
                        selectedReplacements = viewModel.GetSelectedReplacements().ToList();
                    }
                });

                if (dialogResult == ModReplacementDialogResult.Download && selectedReplacements != null && selectedReplacements.Any())
                {
                    // --- Second Progress Dialog (for API checks) ---
                    int addedCount = 0;
                    int alreadyQueuedCount = 0;
                    int errorCount = 0;
                    var addedNames = new List<string>();

                    await RunOnUIThreadAsync(() =>
                    {
                        progressViewModel = _dialogService.ShowProgressDialog(
                            "Verifying Replacements",
                            "Checking replacement mods with Steam API...",
                            canCancel: true,
                            isIndeterminate: false, // Keep this one determinate
                            cts: null,
                            closeable: true);
                    });
                    if (progressViewModel == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                    // Dispose previous linked CTS and create new one for this stage
                    linkedCts?.Dispose();
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressViewModel.CancellationToken);
                    combinedToken = linkedCts.Token;

                    try
                    {
                        for (int i = 0; i < selectedReplacements.Count; i++)
                        {
                            combinedToken.ThrowIfCancellationRequested();
                            var selectedItem = selectedReplacements[i];

                            // Update progress for the *second* dialog
                            await RunOnUIThreadAsync(() =>
                            {
                                if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                                {
                                    progressViewModel.Message = $"Checking {selectedItem.ReplacementInfo.ReplacementName}... ({i + 1}/{selectedReplacements.Count})";
                                    progressViewModel.Progress = (int)((double)(i + 1) / selectedReplacements.Count * 100);
                                }
                            });

                            // ... (rest of the API checking and queueing logic) ...
                             var replacementInfo = selectedItem.ReplacementInfo;
                            if (replacementInfo == null || string.IsNullOrEmpty(replacementInfo.ReplacementSteamId))
                            {
                                Debug.WriteLine($"[ExecuteCheckReplacements] Skipping selected item without valid ReplacementSteamId: {selectedItem.OriginalMod.Name} -> {replacementInfo?.ReplacementName}");
                                errorCount++;
                                continue;
                            }

                            if (_downloadQueueService.IsInQueue(replacementInfo.ReplacementSteamId))
                            {
                                Debug.WriteLine($"[ExecuteCheckReplacements] Replacement mod '{replacementInfo.ReplacementName}' ({replacementInfo.ReplacementSteamId}) is already in the download queue.");
                                alreadyQueuedCount++;
                                continue;
                            }

                            try
                            {
                                var apiResponse = await _steamApiClient.GetFileDetailsAsync(replacementInfo.ReplacementSteamId, combinedToken);
                                if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
                                {
                                    Debug.WriteLine($"[ExecuteCheckReplacements] No details returned for replacement mod '{replacementInfo.ReplacementName}' ({replacementInfo.ReplacementSteamId})");
                                    errorCount++;
                                    continue;
                                }

                                var details = apiResponse.Response.PublishedFileDetails.First();
                                if (details.Result != 1)
                                {
                                    Debug.WriteLine($"[ExecuteCheckReplacements] API indicated failure retrieving details for replacement mod '{replacementInfo.ReplacementName}' ({replacementInfo.ReplacementSteamId}). Result Code: {details.Result}");
                                    errorCount++;
                                    continue;
                                }

                                DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
                                DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;

                                var modInfoDto = new ModInfoDto
                                {
                                    Name = details.Title ?? replacementInfo.ReplacementName,
                                    SteamId = replacementInfo.ReplacementSteamId,
                                    Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={replacementInfo.ReplacementSteamId}",
                                    PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                                    StandardDate = apiUpdateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                                };

                                if (_downloadQueueService.AddToQueue(modInfoDto))
                                {
                                    addedCount++;
                                    addedNames.Add(modInfoDto.Name);
                                    Debug.WriteLine($"[ExecuteCheckReplacements] Added '{modInfoDto.Name}' ({modInfoDto.SteamId}) to download queue.");
                                }
                                else
                                {
                                    Debug.WriteLine($"[ExecuteCheckReplacements] Failed to add '{modInfoDto.Name}' ({modInfoDto.SteamId}) to queue (returned false).");
                                    errorCount++;
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception apiEx)
                            {
                                Debug.WriteLine($"[ExecuteCheckReplacements] Error checking Steam API for replacement mod '{replacementInfo.ReplacementName}' ({replacementInfo.ReplacementSteamId}): {apiEx.Message}");
                                errorCount++;
                                continue;
                            }
                        } // end for loop

                        // Show summary message on UI Thread
                        await RunOnUIThreadAsync(() =>
                        {
                            if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                            {
                                progressViewModel.CompleteOperation("Replacements processed.");
                            }

                            var sb = new StringBuilder();
                            if (addedCount > 0) sb.AppendLine($"{addedCount} replacement mod(s) added to the download queue.");
                            else sb.AppendLine("No new replacement mods were added to the download queue.");
                            if (alreadyQueuedCount > 0) sb.AppendLine($"{alreadyQueuedCount} selected replacement mod(s) were already in the queue.");
                            if (errorCount > 0) sb.AppendLine($"{errorCount} selected replacement mod(s) could not be added due to errors or missing info.");

                            _dialogService.ShowInformation("Replacements Processed", sb.ToString().Trim());
                            if (addedCount > 0){
                                _navigationService.RequestTabSwitch("Downloader");
                            }
                        });

                    } // end try for API checks
                    finally
                    {
                        await RunOnUIThreadAsync(() => progressViewModel?.ForceClose()); // Close the *second* progress dialog
                    }
                } // end if Download selected

            } // end outer try
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteCheckReplacements] Operation cancelled.");
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose(); // Close whichever progress dialog might be open
                    _dialogService.ShowWarning("Operation Cancelled", "Checking for mod replacements was cancelled.");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking replacements: {ex}");
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose();
                    _dialogService.ShowError("Replacement Error", $"An error occurred while checking for replacements: {ex.Message}");
                });
            }
            finally
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose()); // Ensure final close just in case
                linkedCts?.Dispose();
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