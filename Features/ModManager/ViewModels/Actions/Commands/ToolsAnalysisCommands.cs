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
using System.Collections.Concurrent;

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
                observedProperties: new[] { nameof(IsParentLoading) });

            CheckDuplicatesCommand = CreateCommand(
                ExecuteCheckDuplicates,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            CheckReplacementsCommand = CreateCancellableAsyncCommand(
                ExecuteCheckReplacements,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            StripModsCommand = CreateCancellableAsyncCommand(
                ExecuteStripModsAsync,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            RunGameCommand = CreateCommand(
                ExecuteRunGame,
                CanExecuteRunGame,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });
        }

        private void InitializePlaceholderCommands()
        {

            FixIntegrityCommand = CreateCommand(
                () => _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."),
                () => true,
                nameof(IsParentLoading));
        }

        private bool CanExecuteCheckIncompatibilities() => !IsParentLoading && _modListManager.VirtualActiveMods.Any();

        private async Task ExecuteResolveDependencies(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null;
            CancellationTokenSource? linkedCts = null;

            try
            {
                var result = await Task.Run(() => _modListManager.ResolveDependencies(), ct);
                ct.ThrowIfCancellationRequested();

                var (addedMods, missingDependencies) = result;

                if (missingDependencies.Count > 0)
                {
                    DependencyResolutionDialogResult dialogResult = DependencyResolutionDialogResult.Cancel;
                    List<string>? selectedSteamIds = null;

                    await RunOnUIThreadAsync(() =>
                    {
                        var viewModel = new DependencyResolutionDialogViewModel(missingDependencies);
                        dialogResult = _dialogService.ShowDependencyResolutionDialog(viewModel);
                        if (dialogResult == DependencyResolutionDialogResult.Download)
                        {
                            selectedSteamIds = viewModel.GetSelectedSteamIds();
                        }
                    });

                    ct.ThrowIfCancellationRequested();

                    if (dialogResult == DependencyResolutionDialogResult.Download && selectedSteamIds != null && selectedSteamIds.Any())
                    {
                        QueueProcessResult queueResult = new QueueProcessResult();
                        try
                        {
                            await RunOnUIThreadAsync(() =>
                            {
                                progressDialog = _dialogService.ShowProgressDialog(
                               "Verifying & Queueing Dependencies", "Starting...", canCancel: true,
                               isIndeterminate: false, cts: null, closeable: true);
                            });
                            if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                            var combinedToken = linkedCts.Token;

                            var progressReporter = new Progress<QueueProcessProgress>(update =>
                            {
                                RunOnUIThread(() =>
                                {
                                    if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                                    {
                                        progressDialog.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                                        progressDialog.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                                    }
                                });
                            });

                            queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(selectedSteamIds, progressReporter, combinedToken);

                            await RunOnUIThreadAsync(() =>
                            {
                                if (queueResult.WasCancelled)
                                {
                                    progressDialog?.ForceClose();
                                    _dialogService.ShowWarning("Operation Cancelled", "Queueing missing dependencies was cancelled.");
                                    return;
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
                        catch (OperationCanceledException)
                        {
                            await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                        }
                        finally
                        {
                            await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                            linkedCts?.Dispose();
                            linkedCts = null;
                            progressDialog = null;
                        }
                    }
                    else if (addedMods.Count > 0)
                    {
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
                }
                else if (addedMods.Count > 0)
                {
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
                    await RunOnUIThreadAsync(() =>
                    {
                        _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found and no new dependencies were activated.");
                    });
                }
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Dependency resolution was cancelled."));
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                RunOnUIThread(() => _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}"));
            }
            finally
            {
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
                RunOnUIThread(() => _dialogService.ShowWarning("Operation Cancelled", "Incompatibility check was cancelled."));
            }
            catch (Exception ex)
            {
                RunOnUIThread(() => _dialogService.ShowError("Compatibility Error", $"Error checking mod incompatibilities: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ExecuteCheckDuplicates()
        {
            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var allMods = _modListManager.GetAllMods().ToList();
                var actualDuplicateGroups = allMods
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
                RunOnUIThread(() => _dialogService.ShowError("Duplicates Error", $"Error checking for duplicate mods: {ex.Message}"));
            }
            finally { IsLoadingRequest?.Invoke(this, false); }
        }

        private void ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(groups, ApplyIncompatibilityResolutions, () => { });
            var dialog = new ModIncompatibilityDialogView(dialogViewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
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
                _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
        }

        private void ShowDuplicateModsDialog(List<IGrouping<string, ModItem>> duplicateGroups)
        {
            var dialogViewModel = new DuplicateModDialogViewModel(duplicateGroups, pathsToDelete => DeleteDuplicateModsAsyncInternal(pathsToDelete), () => { });
            var view = new DuplicateModDialogView(dialogViewModel)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            view.ShowDialog();
        }

        private async Task ExecuteCheckReplacements(CancellationToken ct)
        {
            if (!HasValidPaths || _steamApiClient is null)
            {
                if (_steamApiClient is null) Debug.WriteLine("[ExecuteCheckReplacements] Steam API client is not available.");
                return;
            }

            IsLoadingRequest?.Invoke(this, true);

            ProgressDialogViewModel? progressViewModel = null;
            CancellationTokenSource? linkedCts = null;
            var modsWithValidReplacements = new ConcurrentBag<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate)>();

            try
            {
                await Task.Run(() => _modService.LoadMods(), ct);
                ct.ThrowIfCancellationRequested();
                var loadedMods = _modService.GetLoadedMods().ToList();
                ct.ThrowIfCancellationRequested();

                if (loadedMods == null || !loadedMods.Any())
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No mods loaded to check for replacements."));
                    return;
                }

                await RunOnUIThreadAsync(() =>
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Replacements", "Searching for mod replacements...", canCancel: true,
                        isIndeterminate: false, cts: linkedCts, closeable: true);
                });
                if (progressViewModel == null || linkedCts == null) throw new InvalidOperationException("Progress dialog or CTS was not created.");
                var combinedTokenPhase1 = linkedCts.Token;

                using var semaphore = new SemaphoreSlim(10);
                var tasks = new List<Task>();
                int processedCount = 0;

                var modsToProcess = loadedMods
                    .Select(mod => new { Mod = mod, Replacement = _replacementService.GetReplacementBySteamId(mod.SteamId ?? "") })
                    .Where(x => x.Replacement != null && !string.IsNullOrEmpty(x.Mod.SteamId) && !string.IsNullOrEmpty(x.Replacement.ReplacementSteamId))
                    .ToList();

                if (!modsToProcess.Any())
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No known replacement rules apply to your loaded mods."));
                    return;
                }

                foreach (var item in modsToProcess)
                {
                    await semaphore.WaitAsync(combinedTokenPhase1);
                    combinedTokenPhase1.ThrowIfCancellationRequested();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var originalModDetailsTask = _steamApiClient.GetFileDetailsAsync(item.Mod.SteamId!, combinedTokenPhase1);
                            var replacementModDetailsTask = _steamApiClient.GetFileDetailsAsync(item.Replacement!.ReplacementSteamId, combinedTokenPhase1);
                            await Task.WhenAll(originalModDetailsTask, replacementModDetailsTask);
                            combinedTokenPhase1.ThrowIfCancellationRequested();

                            var originalApiResponse = await originalModDetailsTask;
                            var replacementApiResponse = await replacementModDetailsTask;

                            var replacementDetails = replacementApiResponse?.Response?.PublishedFileDetails?.FirstOrDefault();
                            if (replacementDetails == null || replacementDetails.Result != (int)SteamApiResultCode.OK)
                            {
                                string reason = SteamApiResultHelper.GetDescription(replacementDetails?.Result ?? (int)SteamApiResultCode.FileNotFound);
                                Debug.WriteLine($"[CheckReplacements] Discarding replacement for '{item.Mod.Name}' because replacement mod ({item.Replacement.ReplacementSteamId}) is not available: {reason}");
                                return;
                            }

                            var originalDetails = originalApiResponse?.Response?.PublishedFileDetails?.FirstOrDefault();
                            if (originalDetails == null || originalDetails.Result != (int)SteamApiResultCode.OK)
                            {
                                Debug.WriteLine($"[CheckReplacements] Discarding replacement for '{item.Mod.Name}' because its own Steam details could not be fetched.");
                                return;
                            }

                            var originalVersions = SteamApiResultHelper.ExtractAndSortVersionTags(originalDetails.Tags);
                            var replacementVersions = SteamApiResultHelper.ExtractAndSortVersionTags(replacementDetails.Tags);

                            if (originalVersions.Any() && replacementVersions.Any())
                            {
                                var latestOriginalVersion = new Version(originalVersions.Last());
                                var latestReplacementVersion = new Version(replacementVersions.Last());

                                if (latestReplacementVersion < latestOriginalVersion)
                                {
                                    Debug.WriteLine($"[CheckReplacements] Discarding replacement for '{item.Mod.Name}' due to lower version ({latestReplacementVersion} < {latestOriginalVersion}).");
                                    return;
                                }

                                if (latestReplacementVersion == latestOriginalVersion && replacementDetails.TimeUpdated < originalDetails.TimeUpdated)
                                {
                                    Debug.WriteLine($"[CheckReplacements] Discarding replacement for '{item.Mod.Name}' due to older update date on same max version.");
                                    return;
                                }
                            }
                            else if (originalVersions.Any() && !replacementVersions.Any())
                            {
                                Debug.WriteLine($"[CheckReplacements] Discarding replacement for '{item.Mod.Name}' because it has no version tags while original does.");
                                return;
                            }

                            modsWithValidReplacements.Add((item.Mod, item.Replacement, originalDetails.TimeUpdated, replacementDetails.TimeUpdated));
                        }
                        catch (OperationCanceledException) { /* Task cancelled */ }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CheckReplacements] Error processing replacement for {item.Mod.Name}: {ex.Message}");
                        }
                        finally
                        {
                            var current = Interlocked.Increment(ref processedCount);
                            RunOnUIThread(() =>
                            {
                                if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                                {
                                    progressViewModel.Message = $"Validating {current}/{modsToProcess.Count}: {item.Mod.Name}";
                                    progressViewModel.Progress = (int)((double)current / modsToProcess.Count * 100);
                                }
                            });
                            semaphore.Release();
                        }
                    }, combinedTokenPhase1));
                }
                await Task.WhenAll(tasks);

                await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Validation complete."));
                progressViewModel = null;
                linkedCts?.Dispose();
                linkedCts = null;

                ct.ThrowIfCancellationRequested();

                var validReplacementsList = modsWithValidReplacements.ToList();
                if (validReplacementsList.Count == 0)
                {
                    await RunOnUIThreadAsync(() => _dialogService.ShowInformation("Mod Replacements", "No suitable replacement mods found after validation."));
                    return;
                }

                ModReplacementDialogResult dialogResult = ModReplacementDialogResult.Cancel;
                List<string>? selectedReplacementSteamIds = null;

                await RunOnUIThreadAsync(() =>
                {
                    var viewModel = new ModReplacementDialogViewModel(validReplacementsList, loadedMods);
                    dialogResult = _dialogService.ShowModReplacementDialog(viewModel);
                    if (dialogResult == ModReplacementDialogResult.Download)
                    {
                        selectedReplacementSteamIds = viewModel.GetSelectedReplacements()
                            .Select(r => r.ReplacementInfo?.ReplacementSteamId)
                            .Where(id => !string.IsNullOrEmpty(id))
                            .Select(id => id!)
                            .ToList();
                    }
                });

                ct.ThrowIfCancellationRequested();

                if (dialogResult == ModReplacementDialogResult.Download && selectedReplacementSteamIds != null && selectedReplacementSteamIds.Any())
                {
                    QueueProcessResult queueResult = new QueueProcessResult();
                    try
                    {
                        await RunOnUIThreadAsync(() =>
                        {
                            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            progressViewModel = _dialogService.ShowProgressDialog(
                              "Verifying & Queueing Replacements", "Starting...", canCancel: true,
                              isIndeterminate: false, cts: linkedCts, closeable: true);
                        });
                        if (progressViewModel == null || linkedCts == null) throw new InvalidOperationException("Progress dialog or CTS was not created.");
                        var combinedTokenPhase2 = linkedCts.Token;

                        var progressReporter = new Progress<QueueProcessProgress>(update =>
                        {
                            RunOnUIThread(() =>
                            {
                                if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                                {
                                    progressViewModel.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                                    progressViewModel.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                                }
                            });
                        });

                        queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(selectedReplacementSteamIds, progressReporter, combinedTokenPhase2);

                        await RunOnUIThreadAsync(() =>
                        {
                            if (queueResult.WasCancelled)
                            {
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
                    catch (OperationCanceledException)
                    {
                        await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                    }
                    finally
                    {
                        await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                        linkedCts?.Dispose();
                        linkedCts = null;
                        progressViewModel = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose();
                    _dialogService.ShowWarning("Operation Cancelled", "Checking for mod replacements was cancelled.");
                });
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() =>
                {
                    progressViewModel?.ForceClose();
                    _dialogService.ShowError("Replacement Error", $"An error occurred while checking for replacements: {ex.Message}");
                });
            }
            finally
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
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