using RimSharp.Core.Commands;
using RimSharp.Shared.Models;
using RimSharp.MyApp.AppFiles;
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

            OpenModFoldersCommand = CreateCommand<IList>(
                OpenModFolders,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

            OpenUrlsCommand = CreateCommand<IList>(
                OpenUrls,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

            OpenWorkshopPagesCommand = CreateCommand<IList>(
                OpenWorkshopPages,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));

            OpenOtherUrlsCommand = CreateCommand<IList>(
                OpenOtherUrls,
                CanExecuteMultiSelectActions,
                nameof(SelectedItems), nameof(IsParentLoading));
        }

        private bool CanExecuteDeleteMod(ModItem mod)
        {
            mod = mod ?? SelectedMod;
            return CanBeDeleted(mod) && !string.IsNullOrEmpty(mod?.Path) && !IsParentLoading;
        }

        private bool CanExecuteDeleteMods(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems;
            return selectedItems != null
                && selectedItems.Count > 0
                && selectedItems.Cast<ModItem>().Any(CanBeDeleted)
                && !IsParentLoading;
        }

        private bool CanExecuteMultiSelectActions(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems;
            return selectedItems != null && selectedItems.Count > 0 && !IsParentLoading;
        }

        private async Task ExecuteDeleteModAsync(ModItem mod, CancellationToken ct)
        {
            await DeleteSingleModAsyncInternal(mod ?? SelectedMod, ct);
        }

        private async Task ExecuteDeleteModsAsync(IList selectedItems, CancellationToken ct)
        {
            await DeleteMultipleModsAsyncInternal(selectedItems ?? SelectedItems, ct);
        }

        private void OpenModFolders(IList selectedItems)
        {
            OpenItems(selectedItems ?? SelectedItems, m => m.Path, "folders", Directory.Exists);
        }

        private void OpenUrls(IList selectedItems)
        {
            OpenItems(selectedItems ?? SelectedItems, m => m.Url, "URLs");
        }

        private void OpenWorkshopPages(IList selectedItems)
        {
            OpenItems(selectedItems ?? SelectedItems, m => m.SteamUrl, "workshop pages");
        }

        private void OpenOtherUrls(IList selectedItems)
        {
            OpenItems(selectedItems ?? SelectedItems, m => m.ExternalUrl, "external URLs");
        }
    }
}