using RimSharp.Core.Commands;
using RimSharp.Shared.Models;
using RimSharp.MyApp.AppFiles; // For ViewModelBase/RunOnUIThread if needed
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Features.ModManager.ViewModels.Actions
{
    // Mark the class as partial
    public partial class ModActionsViewModel
    {
        // Partial initialization method
        private void InitializeModActionsCommands()
        {
            DeleteModCommand = new AsyncRelayCommand<ModItem>(ExecuteDeleteModAsync, CanExecuteDeleteMod);
            DeleteModsCommand = new AsyncRelayCommand<IList>(ExecuteDeleteModsAsync, CanExecuteDeleteMods);
            OpenModFoldersCommand = new RelayCommand<IList>(OpenModFolders, CanExecuteMultiSelectActions);
            OpenUrlsCommand = new RelayCommand<IList>(OpenUrls, CanExecuteMultiSelectActions);
            OpenWorkshopPagesCommand = new RelayCommand<IList>(OpenWorkshopPages, CanExecuteMultiSelectActions);
            OpenOtherUrlsCommand = new RelayCommand<IList>(OpenOtherUrls, CanExecuteMultiSelectActions);
        }

        // --- CanExecute Methods ---
        private bool CanExecuteDeleteMod(ModItem mod)
        {
            mod = mod ?? SelectedMod;
            // Uses the CanBeDeleted helper method defined in ModDeletionHelper.cs
            return CanBeDeleted(mod) && !string.IsNullOrEmpty(mod.Path) && Directory.Exists(mod.Path) && !IsParentLoading;
        }

        private bool CanExecuteDeleteMods(IList selectedItems)
        {
            selectedItems = selectedItems ?? SelectedItems;
            // Uses the CanBeDeleted helper method defined in ModDeletionHelper.cs
            return selectedItems != null
                && selectedItems.Count > 0
                && selectedItems.Cast<ModItem>().Any(CanBeDeleted)
                && !IsParentLoading;
        }

        // --- Execution Methods (Call Helpers) ---
        private async Task ExecuteDeleteModAsync(ModItem mod, CancellationToken ct = default)
        {
            // Calls the helper method defined in ModDeletionHelper.cs
            await DeleteSingleModAsyncInternal(mod, ct);
        }

        private async Task ExecuteDeleteModsAsync(IList selectedItems, CancellationToken ct = default)
        {
            // Calls the helper method defined in ModDeletionHelper.cs
            await DeleteMultipleModsAsyncInternal(selectedItems, ct);
        }

        private void OpenModFolders(IList selectedItems)
        {
            // Calls the helper method defined in ModOpeningHelper.cs
            OpenItems(selectedItems ?? SelectedItems, m => m.Path, "folders", Directory.Exists);
        }

        private void OpenUrls(IList selectedItems)
        {
            // Calls the helper method defined in ModOpeningHelper.cs
            OpenItems(selectedItems ?? SelectedItems, m => m.Url, "URLs");
        }

        private void OpenWorkshopPages(IList selectedItems)
        {
             // Calls the helper method defined in ModOpeningHelper.cs
            OpenItems(selectedItems ?? SelectedItems, m => m.SteamUrl, "workshop pages");
        }

        private void OpenOtherUrls(IList selectedItems)
        {
             // Calls the helper method defined in ModOpeningHelper.cs
            OpenItems(selectedItems ?? SelectedItems, m => m.ExternalUrl, "external URLs");
        }
    }
}