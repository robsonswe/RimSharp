using RimSharp.Core.Commands;
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
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                ct.ThrowIfCancellationRequested();

                var (addedMods, missingDependencies) = result;

                var message = new StringBuilder();
                if (addedMods.Count > 0)
                {
                    message.AppendLine("Dependencies automatically added:");
                    message.AppendLine();
                    foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                    message.AppendLine();
                }
                if (missingDependencies.Count > 0)
                {
                    message.AppendLine("MISSING Dependencies:");
                    message.AppendLine();
                    foreach (var dep in missingDependencies)
                    {
                        message.AppendLine($"- {dep.displayName} ({dep.packageId})");
                        message.AppendLine($"  Required by: {string.Join(", ", dep.requiredBy)}");
                        if (!string.IsNullOrEmpty(dep.steamUrl)) message.AppendLine($"  Workshop URL: {dep.steamUrl}");
                        message.AppendLine();
                    }
                }

                await RunOnUIThreadAsync(() =>
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
            // This list will now only contain *valid* replacements after the version check
            List<(ModItem Original, ModReplacementInfo Replacement)> modsWithValidReplacements = new List<(ModItem, ModReplacementInfo)>();

            try
            {
                // Get loaded mods (can take time)
                // Ensure we get a fresh list if things might have changed
                await Task.Run(() => _modService.LoadMods(), ct); // Or LoadModsAsync if preferred and handled
                ct.ThrowIfCancellationRequested();
                var loadedMods = _modService.GetLoadedMods().ToList(); // Get the list after loading/refreshing
                ct.ThrowIfCancellationRequested();

                if (loadedMods == null || !loadedMods.Any())
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No mods loaded to check for replacements."));
                    return;
                }

                // Show Progress Dialog on UI Thread
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Replacements",
                        "Searching for mod replacements...",
                        canCancel: true,
                        isIndeterminate: false, // We can show progress now
                        cts: null,
                        closeable: true);
                });

                if (progressViewModel == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressViewModel.CancellationToken);
                var combinedToken = linkedCts.Token;

                // Perform the search and validation operation
                await Task.Run(async () => // Mark lambda as async if needed for UI updates inside
                {
                    int processedCount = 0;
                    foreach (var mod in loadedMods)
                    {
                        combinedToken.ThrowIfCancellationRequested();
                        processedCount++;

                        // Update progress within the task
                        await RunOnUIThreadAsync(() =>
                        {
                            if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested) 
                             {
                                progressViewModel.Message = $"Checking {mod.Name}... ({processedCount}/{loadedMods.Count})";
                                progressViewModel.Progress = (int)((double)processedCount / loadedMods.Count * 100);
                             }
                        });


                        ModReplacementInfo replacement = null;
                        // Find potential replacement based on the original mod's SteamId
                        if (!string.IsNullOrEmpty(mod.SteamId))
                        {
                            replacement = _replacementService.GetReplacementBySteamId(mod.SteamId);
                        }
                        // Optional: Add fallback to PackageId if needed and reliable
                        // if (replacement == null && !string.IsNullOrEmpty(mod.PackageId))
                        // {
                        //     replacement = _replacementService.GetReplacementByPackageId(mod.PackageId);
                        // }

                        // --- VERSION VALIDATION ---
                        if (replacement != null)
                        {
                            // Get versions supported by the ORIGINAL mod from its ModItem data
                            var originalSupportedVersions = mod.SupportedVersionStrings?
                                .Select(v => v.Trim())
                                .Where(v => !string.IsNullOrEmpty(v))
                                .ToList() ?? new List<string>();

                            // Get versions supported by the REPLACEMENT mod from the rule data
                            var replacementSupportedVersions = replacement.ReplacementVersionList; // Already parsed list

                            // If the original mod lists no specific versions, we can't filter based on this. Assume rule is okay.
                            // Or, if replacement lists no versions (less likely), assume okay? Or filter out? Let's filter out if original *has* versions.
                            bool versionsAreValid;
                            if (!originalSupportedVersions.Any())
                            {
                                versionsAreValid = true; // Original supports nothing specific, so replacement is fine.
                            }
                            else if (!replacementSupportedVersions.Any())
                            {
                                versionsAreValid = false; // Original needs specific versions, replacement lists none.
                            }
                            else
                            {
                                // Use HashSet for efficient check: Does replacement support ALL original versions?
                                var replacementVersionSet = new HashSet<string>(replacementSupportedVersions, StringComparer.OrdinalIgnoreCase);
                                versionsAreValid = originalSupportedVersions.All(ov => replacementVersionSet.Contains(ov));
                            }


                            if (versionsAreValid)
                            {
                                // Only add if the replacement covers all necessary versions
                                modsWithValidReplacements.Add((mod, replacement));
                            }
                            else
                            {
                                // Log discarded rule (optional)
                                Debug.WriteLine($"[ExecuteCheckReplacements] Discarding outdated rule for '{mod.Name}' ({mod.SteamId}). " +
                                                $"Original versions: [{string.Join(", ", originalSupportedVersions)}]. " +
                                                $"Replacement versions: [{string.Join(", ", replacementSupportedVersions)}].");
                            }
                        }
                        // --- END VERSION VALIDATION ---
                    }
                }, combinedToken);

                // Close progress dialog cleanly
                await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Check complete."));
                progressViewModel = null; // Prevent further interaction

                // Use the filtered list 'modsWithValidReplacements' now
                if (modsWithValidReplacements.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No suitable replacement mods found for your current mod list (considering version compatibility)."));
                    return;
                }

                // --- Show Dialog and Handle Result (using the filtered list) ---
                ModReplacementDialogResult dialogResult = ModReplacementDialogResult.Cancel;
                List<ReplacementSelectionViewModel> selectedReplacements = null;

                await RunOnUIThreadAsync(() =>
                {
                    // Pass the *filtered* list and *all* loaded mods (for the "already installed" check context)
                    var viewModel = new ModReplacementDialogViewModel(modsWithValidReplacements, loadedMods);
                    dialogResult = _dialogService.ShowModReplacementDialog(viewModel);

                    if (dialogResult == ModReplacementDialogResult.Download)
                    {
                        selectedReplacements = viewModel.GetSelectedReplacements().ToList();
                    }
                });

                // --- Process selected replacements (unchanged from here, as it uses selectedReplacements) ---
                if (dialogResult == ModReplacementDialogResult.Download && selectedReplacements != null && selectedReplacements.Any())
                {
                   // ... rest of the download queue logic remains the same ...
                    int addedCount = 0;
                    int alreadyQueuedCount = 0;
                    int errorCount = 0;
                    var addedNames = new List<string>();

                    // Show new progress dialog for Steam API checks
                    await RunOnUIThreadAsync(() =>
                    {
                        progressViewModel = _dialogService.ShowProgressDialog(
                            "Verifying Replacements",
                            "Checking replacement mods with Steam API...",
                            canCancel: true,
                            isIndeterminate: false,
                            cts: null,
                            closeable: true);
                    });
                     if (progressViewModel == null) throw new InvalidOperationException("Progress dialog view model was not created.");


                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressViewModel.CancellationToken);
                    combinedToken = linkedCts.Token;

                    try
                    {
                        // Process each selected replacement
                        for (int i = 0; i < selectedReplacements.Count; i++)
                        {
                            combinedToken.ThrowIfCancellationRequested();
                            var selectedItem = selectedReplacements[i];

                            // Update progress
                            await RunOnUIThreadAsync(() =>
                            {
                                if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                                {
                                    progressViewModel.Message = $"Checking {selectedItem.ReplacementInfo.ReplacementName}... ({i + 1}/{selectedReplacements.Count})";
                                    progressViewModel.Progress = (int)((double)(i + 1) / selectedReplacements.Count * 100);
                                }
                            });

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

                            // Get details from Steam API
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

                                // Convert Steam timestamp to DateTime
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
                            catch (OperationCanceledException)
                            {
                                throw; // Re-throw cancellation
                            }
                            catch (Exception apiEx)
                            {
                                Debug.WriteLine($"[ExecuteCheckReplacements] Error checking Steam API for replacement mod '{replacementInfo.ReplacementName}' ({replacementInfo.ReplacementSteamId}): {apiEx.Message}");
                                errorCount++;
                                continue; // Continue with the next selected replacement
                            }
                        }

                        // Show summary message on UI Thread
                        await RunOnUIThreadAsync(() =>
                        {
                            if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested) // Check if progress dialog is still active
                            {
                                progressViewModel.CompleteOperation("Replacements processed."); // Update message before showing summary
                            }

                            var sb = new StringBuilder();
                            if (addedCount > 0)
                            {
                                sb.AppendLine($"{addedCount} replacement mod(s) added to the download queue. Check the Downloader tab.");
                            }
                            else
                            {
                                sb.AppendLine("No new replacement mods were added to the download queue.");
                            }

                            if (alreadyQueuedCount > 0)
                            {
                                sb.AppendLine($"{alreadyQueuedCount} selected replacement mod(s) were already in the queue.");
                            }
                            if (errorCount > 0)
                            {
                                sb.AppendLine($"{errorCount} selected replacement mod(s) could not be added due to errors or missing info.");
                            }

                            _dialogService.ShowInformation("Replacements Processed", sb.ToString().Trim());
                        });
                    }
                    finally
                    {
                         // Ensure the progress dialog is closed even if errors occurred during API calls
                        await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ExecuteCheckReplacements] Operation cancelled.");
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose(); // Close progress if open
                    _dialogService.ShowWarning("Operation Cancelled", "Checking for mod replacements was cancelled.");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking replacements: {ex}");
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose(); // Close progress if open
                    _dialogService.ShowError("Replacement Error", $"An error occurred while checking for replacements: {ex.Message}");
                });
            }
            finally
            {
                // Ensure progress dialog is closed and CTS is disposed
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                linkedCts?.Dispose(); // Dispose linked CTS
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