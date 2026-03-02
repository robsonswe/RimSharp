using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using RimSharp.Shared.Services.Contracts;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Services.Commands
{
    public class ModCommandService : IModCommandService
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;
        private readonly IModFilterService _modFilterService;

        public ModCommandService(
            IModListManager modListManager, 
            IDialogService dialogService,
            IModFilterService modFilterService)
        {
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _modFilterService = modFilterService ?? throw new ArgumentNullException(nameof(modFilterService));
        }

        public Task HandleDropCommand(DropModArgs args)
        {
            if (args?.DroppedItems == null || !args.DroppedItems.Any() || string.IsNullOrEmpty(args.TargetListName))
            {
                Debug.WriteLine("HandleDropCommand: Invalid arguments (null/empty list or missing target).");
                return Task.CompletedTask;
            }

            Debug.WriteLine($"HandleDropCommand: Processing {args.DroppedItems.Count} items for target '{args.TargetListName}' at index {args.DropIndex}.");

            switch (args.TargetListName.ToLowerInvariant())
            {
                case "active":
                    HandleDropOnActiveList(args.DroppedItems, args.DropIndex);
                    break;

                case "inactive":
                    HandleDropOnInactiveList(args.DroppedItems);
                    break;

                default:
                    Debug.WriteLine($"HandleDropCommand: Unrecognized target list name '{args.TargetListName}'.");
                    break;
            }

            return Task.CompletedTask;
        }

        private void HandleDropOnActiveList(List<ModItem> draggedMods, int dropIndex)
        {
            int actualIndex = TranslateFilteredIndexToActualIndex(dropIndex);

            bool allDraggedModsAreActive = draggedMods.All(mod => _modListManager.IsModActive(mod));

            if (allDraggedModsAreActive)
            {
                Debug.WriteLine($"HandleDropOnActiveList: Reordering {draggedMods.Count} active mods to index {actualIndex}.");
                _modListManager.ReorderMods(draggedMods, actualIndex);
            }
            else
            {
                var modsToActivate = draggedMods;
                if (modsToActivate.Any())
                {
                    Debug.WriteLine($"HandleDropOnActiveList: Activating {modsToActivate.Count} mods at index {actualIndex}.");
                    _modListManager.ActivateModsAt(modsToActivate, actualIndex);
                }
                else
                {
                    Debug.WriteLine("HandleDropOnActiveList: No mods needed activation.");
                }
            }
        }

        private int TranslateFilteredIndexToActualIndex(int filteredIndex)
        {
            if (!_modFilterService.ActiveFilterCriteria.IsActive())
            {
                return filteredIndex;
            }

            if (filteredIndex >= 0 && filteredIndex < _modFilterService.ActiveMods.Count)
            {
                var modAtFilteredIndex = _modFilterService.ActiveMods[filteredIndex];

                var allActiveMods = _modListManager.VirtualActiveMods;

                for (int i = 0; i < allActiveMods.Count; i++)
                {
                    if (allActiveMods[i].Mod.PackageId == modAtFilteredIndex.PackageId)
                    {
                        return i;
                    }
                }

                if (filteredIndex >= _modFilterService.ActiveMods.Count)
                {
                    return allActiveMods.Count;
                }
            }

            return filteredIndex;
        }

        private void HandleDropOnInactiveList(List<ModItem> draggedMods)
        {
            var modsToDeactivate = draggedMods.Where(mod => _modListManager.IsModActive(mod)).ToList();

            if (modsToDeactivate.Any())
            {
                Debug.WriteLine($"HandleDropOnInactiveList: Deactivating {modsToDeactivate.Count} mods.");
                _modListManager.DeactivateMods(modsToDeactivate);
            }
            else
            {
                Debug.WriteLine("HandleDropOnInactiveList: No active mods in the dragged list to deactivate.");
            }
        }

        public async Task ClearActiveModsAsync()
        {
            var result = await _dialogService.ShowConfirmationAsync(
                "Confirm Clear",
                "This will remove all non-Core and non-Expansion mods from the active list.\nAre you sure?",
                showCancel: true);

            if (result != MessageDialogResult.OK && result != MessageDialogResult.Yes) return;

            await Task.Run(() => _modListManager.ClearActiveList());
        }

        public async Task SortActiveModsAsync()
        {
            try
            {
                Debug.WriteLine("SortActiveModsAsync: Requesting sort from ModListManager.");
                bool orderChanged = await Task.Run(() => _modListManager.SortActiveList());
                Debug.WriteLine($"SortActiveModsAsync: ModListManager returned orderChanged={orderChanged}");

                if (orderChanged)
                {
                    await _dialogService.ShowInformation(
                        "Sort Complete",
                        "Active mods have been successfully sorted based on dependency rules."
                    );
                }
                else
                {
                    await _dialogService.ShowInformation(
                        "Sort Complete",
                        "Mods are already in the correct order based on dependency rules."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sorting mods: {ex}");
                await _dialogService.ShowError(
                    "Sorting Error",
                    $"Failed to sort mods due to an error: {ex.Message}\n\n" +
                    "This might be caused by circular dependencies between mods. " +
                    "Please check the logs for more details."
                );
            }
        }
    }
}
