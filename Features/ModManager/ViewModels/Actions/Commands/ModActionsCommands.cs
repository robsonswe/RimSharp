using RimSharp.Core.Commands;
using RimSharp.Shared.Models;
using RimSharp.AppDir.AppFiles;
using System;
using System.Collections;
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

            OpenModFoldersCommand = CreateAsyncCommand<IList>(
                OpenModFoldersAsync,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

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
    }
}