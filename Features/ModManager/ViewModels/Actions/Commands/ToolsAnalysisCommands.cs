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
using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

            CheckDuplicatesCommand = CreateCancellableAsyncCommand(
                ExecuteCheckDuplicates,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            CheckAIIncompatibilitiesCommand = CreateAsyncCommand<ModItem>(
                ExecuteCheckAIIncompatibilities,
                canExecute: mod => mod != null,
                observedProperties: new[] { nameof(IsParentLoading) });

            CheckReplacementsCommand = CreateCancellableAsyncCommand(
                ExecuteCheckReplacements,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            StripModsCommand = CreateCancellableAsyncCommand(
                ExecuteStripModsAsync,
                CanExecuteSimpleCommands,
                observedProperties: new[] { nameof(IsParentLoading), nameof(HasValidPaths) });

            OpenLogViewerCommand = CreateAsyncCommand(
                async () => await _dialogService.ShowLogViewerAsync(),
                () => !IsParentLoading,
                nameof(IsParentLoading));

            RunGameCommand = CreateCommand(
                ExecuteRunGame,
                CanExecuteRunGame,
                observedProperties: new[] { nameof(IsParentLoading), nameof(IsLaunchingGame) });
        }

        private void InitializePlaceholderCommands()
        {

            FixIntegrityCommand = CreateCommand(
                async () => await _dialogService.ShowInformation("Not Implemented", "Fix integrity: Functionality not yet implemented."),
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

                    await RunOnUIThreadAsync(async () =>
                    {
                        var viewModel = new DependencyResolutionDialogViewModel(missingDependencies);
                        dialogResult = await _dialogService.ShowDependencyResolutionDialogAsync(viewModel);
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

                            await RunOnUIThreadAsync(async () =>
                            {
                                if (queueResult.WasCancelled)
                                {
                                    progressDialog?.ForceClose();
                                    await _dialogService.ShowWarning("Operation Cancelled", "Queueing missing dependencies was cancelled.");
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
                                await _dialogService.ShowInformation("Dependencies Processed", sb.ToString().Trim());
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
                        await RunOnUIThreadAsync(async () =>
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine("Some required dependencies were missing or not selected for download.");
                            sb.AppendLine();
                            sb.AppendLine($"{addedMods.Count} inactive dependency mod(s) were automatically activated:");
                            foreach (var mod in addedMods.Take(5)) sb.AppendLine($"- {mod.Name}");
                            if (addedMods.Count > 5) sb.AppendLine("  ...");
                            await _dialogService.ShowInformation("Dependencies Resolved (Partial)", sb.ToString().Trim());
                        });
                    }
                }
                else if (addedMods.Count > 0)
                {
                    await RunOnUIThreadAsync(async () =>
                    {
                        var message = new StringBuilder();
                        message.AppendLine("Dependencies automatically activated:");
                        message.AppendLine();
                        foreach (var mod in addedMods) message.AppendLine($"- {mod.Name} ({mod.PackageId})");
                        await _dialogService.ShowInformation("Dependencies Activated", message.ToString().TrimEnd());
                    });
                }
                else
                {
                    await RunOnUIThreadAsync(async () =>
                    {
                        await _dialogService.ShowInformation("Dependencies Check", "No missing dependencies found and no new dependencies were activated.");
                    });
                }
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Dependency resolution was cancelled."));
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Resolution Error", $"Error resolving dependencies: {ex.Message}"));
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
            ProgressDialogViewModel? progressViewModel = null;
            CancellationTokenSource? linkedCts = null;

            try
            {
                await RunOnUIThreadAsync(() =>
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Incompatibilities", "Scanning active mods for issues...", canCancel: true,
                        isIndeterminate: true, cts: linkedCts, closeable: true);
                });
                if (progressViewModel == null || linkedCts == null) throw new InvalidOperationException("Progress dialog or CTS was not created.");
                var combinedToken = linkedCts.Token;

                combinedToken.ThrowIfCancellationRequested();
                var activeMods = _modListManager.VirtualActiveMods.Select(entry => entry.Mod).ToList();
                var incompatibilities = await Task.Run(() => _incompatibilityService.FindIncompatibilities(activeMods), combinedToken);
                combinedToken.ThrowIfCancellationRequested();

                if (incompatibilities.Count == 0)
                {
                    await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("No incompatibilities found."));
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Compatibility Check", "No incompatibilities found."));
                    return;
                }

                RunOnUIThread(() =>
                {
                    if (progressViewModel != null) progressViewModel.Message = "Grouping incompatibilities...";
                });

                var groups = await Task.Run(() => _incompatibilityService.GroupIncompatibilities(incompatibilities), combinedToken);
                combinedToken.ThrowIfCancellationRequested();

                if (groups.Count == 0)
                {
                    await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Analysis complete."));
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Compatibility Check", "Incompatibilities found but could not be grouped for resolution."));
                    return;
                }

                await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Scan complete."));
                await ShowIncompatibilityDialog(groups);
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Incompatibility check was cancelled."));
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Compatibility Error", $"Error checking mod incompatibilities: {ex.Message}"));
            }
            finally 
            { 
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false); 
            }
        }

        private async Task ExecuteCheckDuplicates(CancellationToken ct)
        {
            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressViewModel = null;
            CancellationTokenSource? linkedCts = null;

            try
            {
                await RunOnUIThreadAsync(() =>
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Duplicates", "Scanning all mods for duplicates...", canCancel: true,
                        isIndeterminate: true, cts: linkedCts, closeable: true);
                });
                if (progressViewModel == null || linkedCts == null) throw new InvalidOperationException("Progress dialog or CTS was not created.");
                var combinedToken = linkedCts.Token;

                combinedToken.ThrowIfCancellationRequested();
                var allMods = _modListManager.GetAllMods().ToList();
                var actualDuplicateGroups = await Task.Run(() => _duplicateService.FindDuplicateGroups(allMods), combinedToken);
                combinedToken.ThrowIfCancellationRequested();

                if (actualDuplicateGroups.Any())
                {
                    var dialogViewModel = new DuplicateModDialogViewModel(actualDuplicateGroups, pathsToDelete => DeleteDuplicateModsAsyncInternal(pathsToDelete), () => { });

                    // Only show if we actually have populated groups
                    if (dialogViewModel.DuplicateGroups.Any())
                    {
                        await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("Scan complete."));
                        await _dialogService.ShowDuplicateModsDialogAsync(dialogViewModel);
                    }
                    else
                    {
                        await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("No duplicates found."));
                        await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Duplicates Check", "No duplicate mods found based on Package ID."));
                    }
                }
                else
                {
                    await RunOnUIThreadAsync(() => progressViewModel?.CompleteOperation("No duplicates found."));
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Duplicates Check", "No duplicate mods found based on Package ID."));
                }
            }
            catch (OperationCanceledException)
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowWarning("Operation Cancelled", "Duplicate check was cancelled."));
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("Duplicates Error", $"Error checking for duplicate mods: {ex.Message}"));
            }
            finally 
            { 
                await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                linkedCts?.Dispose();
                IsLoadingRequest?.Invoke(this, false); 
            }
        }

        private async Task ShowIncompatibilityDialog(List<IncompatibilityGroup> groups)
        {
            var dialogViewModel = new ModIncompatibilityDialogViewModel(groups, ApplyIncompatibilityResolutions, () => { });
            await _dialogService.ShowIncompatibilityDialogAsync(dialogViewModel);
        }

        private async void ApplyIncompatibilityResolutions(List<ModItem> modsToRemove)
        {
            if (modsToRemove == null || modsToRemove.Count == 0) return;
            try
            {
                _modListManager.DeactivateMods(modsToRemove);
                await _dialogService.ShowInformation("Incompatibilities Resolved", $"Resolved incompatibilities by deactivating {modsToRemove.Count} mod(s).");
            }
            catch (Exception ex)
            {
                await _dialogService.ShowError("Resolution Error", $"Error applying incompatibility resolutions: {ex.Message}");
            }
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
            var modsWithValidReplacements = new ConcurrentBag<(ModItem Original, ModReplacementInfo Replacement, long OriginalUpdate, long ReplacementUpdate, string ReplacementSteamTitle, string ReplacementSteamAuthor)>();
            var personaNameCache = new ConcurrentDictionary<string, string?>();

            try
            {
                // 1. Show Progress Dialog EARLY
                await RunOnUIThreadAsync(() =>
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    progressViewModel = _dialogService.ShowProgressDialog(
                        "Checking Replacements", "Loading mods and checking rules...", canCancel: true,
                        isIndeterminate: false, cts: linkedCts, closeable: true);
                });
                if (progressViewModel == null || linkedCts == null) throw new InvalidOperationException("Progress dialog or CTS was not created.");
                var combinedTokenPhase1 = linkedCts.Token;
                var loadProgressReporter = new Progress<(int current, int total, string message)>(update =>
                {
                    RunOnUIThread(() =>
                    {
                        if (progressViewModel != null && !progressViewModel.CancellationToken.IsCancellationRequested)
                        {
                            progressViewModel.Message = update.message;
                            progressViewModel.Progress = (int)((double)update.current / update.total * 95); // 0-95% for loading
                        }
                    });
                });
                await _modService.LoadModsAsync(loadProgressReporter);
                combinedTokenPhase1.ThrowIfCancellationRequested();
                var loadedMods = _modService.GetLoadedMods().ToList();
                combinedTokenPhase1.ThrowIfCancellationRequested();

                if (loadedMods == null || !loadedMods.Any())
                {
                    await RunOnUIThreadAsync(() => progressViewModel?.ForceClose());
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Mod Replacements", "No mods loaded to check for replacements."));
                    return;
                }

                // 4. Searching phase
                RunOnUIThread(() =>
                {
                    if (progressViewModel != null)
                    {
                        progressViewModel.Message = "Searching for mod replacements...";
                        progressViewModel.Progress = 95;
                    }
                });

                using var semaphore = new SemaphoreSlim(10);
                var tasks = new List<Task>();
                int processedCount = 0;

                var modsToProcess = loadedMods
                    .Select(mod =>
                    {
                        // 1. Try lookup by Steam ID
                        var r = _replacementService.GetReplacementBySteamId(mod.SteamId ?? "");

                        if (r == null && !string.IsNullOrEmpty(mod.PackageId))
                        {
                            r = _replacementService.GetReplacementByPackageId(mod.PackageId);
                        }

                        return new { Mod = mod, Replacement = r };
                    })
                    .Where(x => x.Replacement != null && 
                                !string.IsNullOrEmpty(x.Mod.SteamId) && 
                                !string.IsNullOrEmpty(x.Replacement.ReplacementSteamId))
                    .ToList();

                Debug.WriteLine($"[CheckReplacements] Found {modsToProcess.Count} potential replacement rules.");

                if (!modsToProcess.Any())
                {
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Mod Replacements", "No known replacement rules apply to your loaded mods."));
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

                            string? steamAuthor = null;
                            if (!string.IsNullOrEmpty(replacementDetails.Creator))
                            {
                                if (!personaNameCache.TryGetValue(replacementDetails.Creator, out steamAuthor))
                                {
                                    steamAuthor = await _steamApiClient.GetPersonaNameAsync(replacementDetails.Creator, combinedTokenPhase1);
                                    personaNameCache[replacementDetails.Creator] = steamAuthor;
                                }
                            }

                            modsWithValidReplacements.Add((item.Mod, item.Replacement, originalDetails.TimeUpdated, replacementDetails.TimeUpdated, replacementDetails.Title, steamAuthor ?? string.Empty));
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
                                    progressViewModel.Progress = 95 + (int)((double)current / modsToProcess.Count * 5);
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
                Debug.WriteLine($"[CheckReplacements] Final validated replacements count: {validReplacementsList.Count}");

                if (validReplacementsList.Count == 0)
                {
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation("Mod Replacements", "No suitable replacement mods found after validation."));
                    return;
                }

                ModReplacementDialogResult dialogResult = ModReplacementDialogResult.Cancel;
                List<string>? selectedReplacementSteamIds = null;

                await RunOnUIThreadAsync(async () =>
                {
                    var viewModel = new ModReplacementDialogViewModel(validReplacementsList, loadedMods);

                    if ((viewModel.Replacements?.Any() ?? false) || viewModel.HasAlreadyInstalledReplacements)
                    {
                        dialogResult = await _dialogService.ShowModReplacementDialogAsync(viewModel);
                        if (dialogResult == ModReplacementDialogResult.Download)
                        {
                            selectedReplacementSteamIds = viewModel.GetSelectedReplacements()
                                .Select(r => r.ReplacementInfo?.ReplacementSteamId)
                                .Where(id => !string.IsNullOrEmpty(id))
                                .Select(id => id!)
                                .ToList();
                        }
                    }
                    else
                    {
                        await _dialogService.ShowInformation("Mod Replacements", "No suitable replacement mods found after validation.");
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

                        await RunOnUIThreadAsync(async () =>
                        {
                            if (queueResult.WasCancelled)
                            {
                                progressViewModel?.ForceClose();
                                await _dialogService.ShowWarning("Operation Cancelled", "Queueing selected replacement mods was cancelled.");
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

                            await _dialogService.ShowInformation("Replacements Processed", sb.ToString().Trim());
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
                await RunOnUIThreadAsync(async () =>
                {
                    progressViewModel?.ForceClose();
                    await _dialogService.ShowWarning("Operation Cancelled", "Checking for mod replacements was cancelled.");
                });
            }
            catch (Exception ex)
            {
                await RunOnUIThreadAsync(async () =>
                {
                    progressViewModel?.ForceClose();
                    await _dialogService.ShowError("Replacement Error", $"An error occurred while checking for replacements: {ex.Message}");
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
            return !IsParentLoading && !IsLaunchingGame;
        }

        private async void ExecuteRunGame()
        {
            if (IsLaunchingGame) return;

            if (!HasValidPaths)
            {
                await _dialogService.ShowError("Invalid Paths", "Game paths are not correctly configured. Please check your settings.");
                return;
            }

            if (IsGameRunning)
            {
                await _dialogService.ShowWarning("Game Already Running", "RimWorld is already running. Please close the existing instance before launching again.");
                return;
            }

            IsLaunchingGame = true;

            try
            {
                var gamePath = _pathService.GetGamePath();
                if (string.IsNullOrEmpty(gamePath))
                {
                    await _dialogService.ShowError("Game Path Error", "Game path is not set.");
                    return;
                }

                string? exePath = null;
                foreach (var candidate in GameExecutableNames.GetLaunchCandidates())
                {
                    var candidatePath = Path.Combine(gamePath, candidate);
                    if (File.Exists(candidatePath) || Directory.Exists(candidatePath))
                    {
                        exePath = candidatePath;
                        break;
                    }
                }

                if (exePath == null)
                {
                    await _dialogService.ShowError("Game Not Found", "Could not find RimWorld executable in the specified game path.");
                    return;
                }

                // macOS: .app bundle launch via `open -n` (no args forwarding; out of scope for v1)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                    exePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    var launched = TryLaunchMacAppBundle(exePath, gamePath);
                    if (!launched)
                    {
                        await _dialogService.ShowError("Launch Error", "Failed to launch RimWorld.app via 'open' and no valid inner executable was found.");
                    }
                }
                else
                {
                    EnsureExecutable(exePath);

                    RimSharp.Core.Services.SystemLauncher.LaunchExecutable(
                        exePath, gamePath,
                        onError: ex => { });
                }

                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowError("Launch Error", $"Failed to launch RimWorld: {ex.Message}");
            }
            finally
            {
                IsLaunchingGame = false;
            }
        }

        /// <summary>
        /// AI suspect analysis for the clicked mod: keywords → full-library
        /// suspect search → provider verdicts → decision dialog.
        /// </summary>
        private async Task ExecuteCheckAIIncompatibilities(ModItem mod)
        {
            if (mod == null) return;

            if (!_aiAgentService.IsConfigured)
            {
                await RunOnUIThreadAsync(() => _dialogService.ShowInformation(
                    "AI Conflict Scan",
                    "AI analysis is not configured yet. Please set an OpenRouter API key in the config (openrouter_api_key)."));
                return;
            }

            IsLoadingRequest?.Invoke(this, true);
            ProgressDialogViewModel? progressDialog = null;
            CancellationTokenSource? analysisCts = null;
            try
            {
                analysisCts = new CancellationTokenSource();
                await RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                        $"AI Conflict Scan — {mod.Name}", "Loading installed mods...", canCancel: true,
                        isIndeterminate: false, cts: analysisCts, closeable: true);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog was not created.");

                // Load the FULL installed library from disk (active or not).
                var allMods = await _dataService.LoadAllModsAsync();
                await RunOnUIThreadAsync(() => progressDialog?.UpdateProgress(
                    8, $"Loaded {allMods.Count} installed mods."));

                var candidates = await Task.Run(
                    () => _aiAgentService.AnalyzeSuspectsForModAsync(
                        mod,
                        allMods,
                        (message, percent) => progressDialog?.UpdateProgress(
                            Math.Clamp((int)Math.Round(percent), 0, 100), message),
                        analysisCts.Token),
                    analysisCts.Token);

                await RunOnUIThreadAsync(() => progressDialog?.CompleteOperation("AI conflict scan complete."));
                if (candidates.Count == 0)
                {
                    await RunOnUIThreadAsync(async () => await _dialogService.ShowInformation(
                        "AI Conflict Scan",
                        $"No indirect incompatibilities found for \"{mod.Name}\"." +
                        $"\n\n{_aiAgentService.LastRunSummary}"));
                    return;
                }

                await RunOnUIThreadAsync(async () =>
                {
                    var vm = new RimSharp.Features.ModManager.Dialogs.AIIncompatibilityReview.AIIncompatibilityReviewDialogViewModel(
                        candidates, ApplyAICandidateAsync);
                    await _dialogService.ShowAIAcausalIncompatibilityReviewAsync(vm);
                });
            }
            catch (Exception ex)
            {
                try { RimSharp.Core.Services.DiagLog.WriteLine($"ANALYSIS FAILED — {ex.GetType().Name}: {ex.Message}"); } catch { }
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await RunOnUIThreadAsync(async () => await _dialogService.ShowError("AI Conflict Scan Error", $"Error during AI analysis: {ex.Message}"));
            }
            finally
            {
                analysisCts?.Cancel();
                await RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        /// <summary>
        /// Persists one AI-confirmed pair as a custom incompatibility rule,
        /// bidirectionally (A→B and B→A), via IModCustomService.
        /// </summary>
        private async Task ApplyAICandidateAsync(RimSharp.Shared.Services.Contracts.AIIncompatibilityCandidate candidate)
        {
            var a = candidate.SourceMod;
            var b = candidate.TargetMod;
            var rule = new RimSharp.Shared.Models.ModIncompatibilityRule
            {
                HardIncompatibility = candidate.Type == RimSharp.Shared.Services.Contracts.AIIncompatibilityType.Hard,
                Comment = { candidate.Reason }
            };

            var infoA = _customService.GetCustomModInfo(a.PackageId) ?? new RimSharp.Shared.Models.ModCustomInfo();
            var infoB = _customService.GetCustomModInfo(b.PackageId) ?? new RimSharp.Shared.Models.ModCustomInfo();

            infoA.IncompatibleWith[b.PackageId] = rule;
            infoB.IncompatibleWith[a.PackageId] = rule;

            await _customService.SaveCustomModInfoAsync(a.PackageId, infoA);
            await _customService.SaveCustomModInfoAsync(b.PackageId, infoB);
        }

        /// <summary>
        /// Archives extracted on Unix may lose the executable bit; restore it
        /// so the game can be launched. On Windows this is a no-op.
        /// </summary>
        private static void EnsureExecutable(string exePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{exePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
            catch (Exception)
            {
                // Launch will surface the permission error if chmod fails.
            }
        }

        /// <summary>
        /// Test seam for macOS .app bundle launch. Returns (launched, mechanism).
        /// Mechanism: "open" for `open -n`, "inner" for Contents/MacOS fallback, "none" for failure.
        /// Tests replace this to avoid spawning real processes.
        /// </summary>
        internal static Func<string, string, (bool Launched, string Mechanism)>? MacAppBundleLauncher { get; set; }

        /// <summary>
        /// Launches a macOS .app bundle via `open -n`. Falls back to the inner
        /// executable under Contents/MacOS/ if `open` fails or the bundle lacks
        /// Info.plist. Returns true on success.
        /// </summary>
        internal static bool TryLaunchMacAppBundle(string appBundlePath, string workingDirectory)
        {
            // Test seam: allow tests to inject behavior without spawning processes
            if (MacAppBundleLauncher != null)
            {
                var (launched, _) = MacAppBundleLauncher(appBundlePath, workingDirectory);
                return launched;
            }

            // Try `open -n` first (preferred: launches a new instance even if one is running)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-n \"{appBundlePath}\"",
                    UseShellExecute = false,
                    WorkingDirectory = workingDirectory
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Fall through to inner binary fallback
            }

            // Fallback: probe Contents/MacOS/ for the main executable
            var macOsDir = Path.Combine(appBundlePath, "Contents", "MacOS");
            if (Directory.Exists(macOsDir))
            {
                var entries = Directory.GetFiles(macOsDir);
                foreach (var entry in entries)
                {
                    // Skip non-executables (e.g., .dSYM, Info.plist)
                    if (File.Exists(entry))
                    {
                        try
                        {
                            EnsureExecutable(entry);
                            var psi = new ProcessStartInfo
                            {
                                FileName = entry,
                                WorkingDirectory = workingDirectory,
                                UseShellExecute = false
                            };
                            using var process = Process.Start(psi);
                            if (process != null)
                            {
                                return true;
                            }
                        }
                        catch (Exception)
                        {
                            // Try next entry
                        }
                    }
                }
            }

            return false;
        }
    }
}


