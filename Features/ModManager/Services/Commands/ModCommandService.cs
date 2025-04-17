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
        private readonly IModFilterService _modFilterService; // Added field for ModFilterService

        // Updated constructor to include IModFilterService
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
            // Updated Check: Use DroppedItems list
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

        // Updated to handle List<ModItem>
        private void HandleDropOnActiveList(List<ModItem> draggedMods, int dropIndex)
        {
            // First, translate filtered index to actual index if filtering is active
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

        // Properly implemented method using the injected ModFilterService
        private int TranslateFilteredIndexToActualIndex(int filteredIndex)
        {
            // If filtering is not active, return the index as-is
            if (!_modFilterService.ActiveFilterCriteria.IsActive())
            {
                return filteredIndex;
            }

            // Get the actual item from the filtered list at filteredIndex
            if (filteredIndex >= 0 && filteredIndex < _modFilterService.ActiveMods.Count)
            {
                var modAtFilteredIndex = _modFilterService.ActiveMods[filteredIndex];

                // Find this mod's position in the full, unfiltered list from ModListManager
                var allActiveMods = _modListManager.VirtualActiveMods;

                for (int i = 0; i < allActiveMods.Count; i++)
                {
                    if (allActiveMods[i].Mod.PackageId == modAtFilteredIndex.PackageId)
                    {
                        return i;
                    }
                }

                // If past the end or not found in the unfiltered list
                if (filteredIndex >= _modFilterService.ActiveMods.Count)
                {
                    return allActiveMods.Count;
                }
            }

            // Default fallback
            return filteredIndex;
        }

        // Updated to handle List<ModItem>
        private void HandleDropOnInactiveList(List<ModItem> draggedMods)
        {
            // Deactivate any of the dragged mods that are currently active.
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
            var result = _dialogService.ShowConfirmation(
                "Confirm Clear",
                "This will remove all non-Core and non-Expansion mods from the active list.\nAre you sure?",
                showCancel: true);

            if (result != MessageDialogResult.OK) return;

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
                    _dialogService.ShowInformation(
                        "Sort Complete",
                        "Active mods have been successfully sorted based on dependency rules."
                    );
                }
                else
                {
                    _dialogService.ShowInformation(
                        "Sort Complete",
                        "Mods are already in the correct order based on dependency rules."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sorting mods: {ex}");
                _dialogService.ShowError(
                    "Sorting Error",
                    $"Failed to sort mods due to an error: {ex.Message}\n\n" +
                    "This might be caused by circular dependencies between mods. " +
                    "Please check the logs for more details."
                );
            }
        }
    }
}