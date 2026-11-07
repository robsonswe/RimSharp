using RimSharp.Core.Commands;
using RimSharp.Shared.Models;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    public partial class ModActionsViewModel
    {
        private void InitializeModActionsCommands()
        {
            DeleteModCommand = CreateAsyncCommand<ModItem>(
                mod => ExecuteDeleteModAsync(mod, CancellationToken.None),
                CanExecuteDeleteMod,
                nameof(SelectedMod), nameof(IsParentLoading));

            DeleteModsCommand = CreateAsyncCommand<IList>(
                items => ExecuteDeleteModsAsync(items, CancellationToken.None),
                CanExecuteDeleteMods,
                nameof(SelectedItems), nameof(IsParentLoading));

            // Delete + unsubscribe from Steam (for Workshop mods)
            DeleteAndUnsubscribeCommand = CreateAsyncCommand<IList>(
                items => ExecuteDeleteAndUnsubscribeAsync(items, CancellationToken.None),
                items => items != null && items.Count > 0 && !IsParentLoading && items.Cast<ModItem>().Any(m => m.ModType == ModType.Workshop),
                nameof(SelectedItems), nameof(IsParentLoading));

            // Delete + resubscribe using Steam (for Workshop mods)
            DeleteAndResubscribeCommand = CreateAsyncCommand<IList>(
                items => ExecuteDeleteAndResubscribeAsync(items, CancellationToken.None),
                items => items != null && items.Count > 0 && !IsParentLoading && items.Cast<ModItem>().Any(m => m.ModType == ModType.Workshop),
                nameof(SelectedItems), nameof(IsParentLoading));

            OpenModFoldersCommand = CreateAsyncCommand<IList>(
                OpenModFoldersAsync,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

            // Verify game files via Steam (steam://validate/294100)
            VerifyGameFilesCommand = CreateAsyncCommand(
                async () => await ExecuteVerifyGameFilesAsync(),
                () => !IsParentLoading && _gameInstance.IsSteamInstallation,
                nameof(IsParentLoading), nameof(_gameInstance.IsSteamInstallation));

            OpenUrlsCommand = CreateAsyncCommand<IList>(
                OpenUrlsAsync,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

            OpenWorkshopPagesCommand = CreateAsyncCommand<IList>(
                OpenWorkshopPagesAsync,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

            OpenOtherUrlsCommand = CreateAsyncCommand<IList>(
                OpenOtherUrlsAsync,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));
        }

        public ICommand DeleteAndUnsubscribeCommand { get; private set; } = null!;
        public ICommand DeleteAndResubscribeCommand { get; private set; } = null!;
        public ICommand VerifyGameFilesCommand { get; private set; } = null!;

        private bool CanExecuteDeleteMod(ModItem? mod)
        {
            mod ??= SelectedMod;
            return mod != null && CanBeDeleted(mod) && !string.IsNullOrEmpty(mod.Path) && !IsParentLoading;
        }

        private bool CanExecuteDeleteMods(IList? selectedItems)
        {
            selectedItems ??= SelectedItems;
            return selectedItems != null
                && selectedItems.Count > 0
                && selectedItems.Cast<ModItem>().Any(CanBeDeleted)
                && !IsParentLoading;
        }

        private bool CanExecuteMultiSelectActions(IList? selectedItems)
        {
            selectedItems ??= SelectedItems;
            return selectedItems != null && selectedItems.Count > 0 && !IsParentLoading;
        }

        private async Task ExecuteDeleteModAsync(ModItem? mod, CancellationToken ct)
        {
            await DeleteSingleModAsyncInternal(mod ?? SelectedMod, ct);
        }

        private async Task ExecuteDeleteModsAsync(IList? selectedItems, CancellationToken ct)
        {
            await DeleteMultipleModsAsyncInternal(selectedItems ?? SelectedItems, ct);
        }

        private async Task OpenModFoldersAsync(IList? selectedItems)
        {
            await OpenItemsAsync(selectedItems ?? SelectedItems, m => m.Path, "folders", Directory.Exists);
        }

        private async Task OpenUrlsAsync(IList? selectedItems)
        {
            await OpenItemsAsync(selectedItems ?? SelectedItems, m => m.Url, "URLs");
        }

        private async Task OpenWorkshopPagesAsync(IList? selectedItems)
        {
            await OpenWorkshopPagesAsyncInternal(selectedItems);
        }

        private async Task OpenWorkshopPagesAsyncInternal(IList? selectedItems)
        {
            await OpenItemsAsync(selectedItems ?? SelectedItems, m => m.SteamUrl, "workshop pages");
        }

        private async Task OpenOtherUrlsAsync(IList? selectedItems)
        {
            await OpenItemsAsync(selectedItems ?? SelectedItems, m => m.ExternalUrl, "external URLs");
        }

        private async Task ExecuteDeleteAndUnsubscribeAsync(IList? selectedItems, CancellationToken ct)
        {
            selectedItems ??= SelectedItems;
            if (selectedItems == null || !selectedItems.Cast<ModItem>().Any()) return;

            var mods = selectedItems.Cast<ModItem>().Where(m => m.ModType == ModType.Workshop && !string.IsNullOrEmpty(m.SteamId)).ToList();
            if (!mods.Any())
            {
                await _dialogService.ShowWarning("No Workshop Mods", "No Steam Workshop mods selected for unsubscribe.");
                return;
            }

            var confirmMessage = $"Delete {mods.Count} mod(s) locally AND unsubscribe from Steam Workshop?\n\n" +
                                "This will:\n" +
                                "• Delete the mod folders from your disk\n" +
                                "• Unsubscribe from these mods on Steam\n\n" +
                                "This action cannot be undone.";

            var result = await _dialogService.ShowConfirmationAsync("Delete & Unsubscribe", confirmMessage, showCancel: true);
            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            if (_steamSubscriptionService == null)
            {
                await _dialogService.ShowError("Service Unavailable", "Steam subscription service is not available.");
                return;
            }

            IsLoadingRequest?.Invoke(this, true);
            try
            {
                foreach (var mod in mods)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    // First unsubscribe from Steam
                    if (!string.IsNullOrEmpty(mod.SteamId))
                    {
                        var progress = new Progress<(int current, int total, string message)>(p => 
                        {
                            _logger.LogDebug($"[Unsubscribe] {p.current}/{p.total}: {p.message}", nameof(ModActionsViewModel));
                        });
                        
                        await _steamSubscriptionService.UnsubscribeAsync(mod.SteamId!, progress, ct);
                    }
                    
                    // Then delete local files
                    if (Directory.Exists(mod.Path))
                    {
                        await _deletionService.DeleteDirectoryRobustAsync(mod.Path, ct);
                    }
                }
                
                // Refresh mod list
                RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                await _dialogService.ShowInformation("Complete", $"Deleted and unsubscribed from {mods.Count} mod(s).");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Delete & Unsubscribe operation cancelled", nameof(ModActionsViewModel));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Delete & Unsubscribe: {ex}", nameof(ModActionsViewModel));
                await _dialogService.ShowError("Error", $"Failed to delete and unsubscribe: {ex.Message}");
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        private async Task ExecuteDeleteAndResubscribeAsync(IList? selectedItems, CancellationToken ct)
        {
            selectedItems ??= SelectedItems;
            if (selectedItems == null || !selectedItems.Cast<ModItem>().Any()) return;

            var mods = selectedItems.Cast<ModItem>().Where(m => m.ModType == ModType.Workshop && !string.IsNullOrEmpty(m.SteamId)).ToList();
            if (!mods.Any())
            {
                await _dialogService.ShowWarning("No Workshop Mods", "No Steam Workshop mods selected for resubscribe.");
                return;
            }

            var confirmMessage = $"Delete {mods.Count} mod(s) locally AND resubscribe via Steam?\n\n" +
                                "This will:\n" +
                                "• Delete the mod folders from your disk\n" +
                                "• Unsubscribe and then resubscribe on Steam (forcing a fresh download)\n\n" +
                                "This is useful for stuck downloads or corrupted files.\n\n" +
                                "This action cannot be undone.";

            var result = await _dialogService.ShowConfirmationAsync("Delete & Resubscribe", confirmMessage, showCancel: true);
            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            if (_steamSubscriptionService == null)
            {
                await _dialogService.ShowError("Service Unavailable", "Steam subscription service is not available.");
                return;
            }

            IsLoadingRequest?.Invoke(this, true);
            try
            {
                foreach (var mod in mods)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    if (!string.IsNullOrEmpty(mod.SteamId))
                    {
                        // Use the resubscribe flow (unsub -> 1s -> sub -> 1s -> DownloadItem highPriority)
                        var progress = new Progress<(int current, int total, string message)>(p => 
                        {
                            _logger.LogDebug($"[Resubscribe] {p.current}/{p.total}: {p.message}", nameof(ModActionsViewModel));
                        });
                        
                        await _steamSubscriptionService.ResubscribeAsync(new[] { mod.SteamId! }, progress, ct);
                    }
                    
                    // Delete local files
                    if (Directory.Exists(mod.Path))
                    {
                        await _deletionService.DeleteDirectoryRobustAsync(mod.Path, ct);
                    }
                }
                
                // Refresh mod list
                RequestDataRefresh?.Invoke(this, EventArgs.Empty);
                await _dialogService.ShowInformation("Complete", $"Deleted and resubscribed {mods.Count} mod(s). Steam will now download fresh copies.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Delete & Resubscribe operation cancelled", nameof(ModActionsViewModel));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Delete & Resubscribe: {ex}", nameof(ModActionsViewModel));
                await _dialogService.ShowError("Error", $"Failed to delete and resubscribe: {ex.Message}");
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
            }
        }

        private async Task ExecuteVerifyGameFilesAsync()
        {
            if (_steamSubscriptionService == null)
            {
                await _dialogService.ShowError("Service Unavailable", "Steam subscription service is not available.");
                return;
            }

            var confirmMessage = "Launch Steam's \"Verify integrity of game files\" for RimWorld?\n\n" +
                                "This will open Steam and start verifying the game files.\n" +
                                "It may take several minutes depending on the game size.";

            var result = await _dialogService.ShowConfirmationAsync("Verify Game Files", confirmMessage, showCancel: true);
            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            IsLoadingRequest?.Invoke(this, true);
            try
            {
                var success = await _steamSubscriptionService.VerifyGameFilesAsync();
                if (success)
                {
                    await _dialogService.ShowInformation("Verify Game Files", "Steam file verification launched. Check Steam for progress.");
                }
                else
                {
                    await _dialogService.ShowError("Error", "Failed to launch Steam file verification. Ensure Steam is running and you own RimWorld.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying game files: {ex}", nameof(ModActionsViewModel));
                await _dialogService.ShowError("Error", $"Failed to verify game files: {ex.Message}");
            }
            finally
            {
                IsLoadingRequest?.Invoke(this, false);
            }
        }
    }
}