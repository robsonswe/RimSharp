using System;
using System.Collections.Generic; // Required for List
using System.Linq; // Required for Linq
using System.Threading.Tasks;
using System.Diagnostics;
using RimSharp.Features.ModManager.Services.Commands;
using RimSharp.Shared.Services.Contracts;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;

namespace RimShaRimSharp.Features.ModManager.Services.Commands
{
    public class ModCommandService : IModCommandService
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService;

        public ModCommandService(IModListManager modListManager, IDialogService dialogService)
        {
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
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
            // Determine if this is primarily a reorder operation within the active list
            // This is true if ALL dragged mods are ALREADY active.
            bool allDraggedModsAreActive = draggedMods.All(mod => _modListManager.IsModActive(mod));

            if (allDraggedModsAreActive)
            {
                // Reorder within active list
                Debug.WriteLine($"HandleDropOnActiveList: Reordering {draggedMods.Count} active mods to index {dropIndex}.");
                _modListManager.ReorderMods(draggedMods, dropIndex); // Requires implementation in ModListManager
            }
            else
            {
                // Activate mods (add inactive mods and potentially reposition existing active ones)
                // Filter out any mods that might already be active if the logic should only add inactive ones.
                // Or, simpler: just ask the manager to ensure all these mods are active at the specified position.
                 var modsToActivate = draggedMods; // Or filter: .Where(m => !_modListManager.IsModActive(m)).ToList();
                if (modsToActivate.Any())
                {
                     Debug.WriteLine($"HandleDropOnActiveList: Activating {modsToActivate.Count} mods at index {dropIndex}.");
                    _modListManager.ActivateModsAt(modsToActivate, dropIndex); // Requires implementation in ModListManager
                }
                else
                {
                    Debug.WriteLine("HandleDropOnActiveList: No mods needed activation.");
                }
            }
        }

        // Updated to handle List<ModItem>
        private void HandleDropOnInactiveList(List<ModItem> draggedMods)
        {
            // Deactivate any of the dragged mods that are currently active.
            var modsToDeactivate = draggedMods.Where(mod => _modListManager.IsModActive(mod)).ToList();

            if (modsToDeactivate.Any())
            {
                Debug.WriteLine($"HandleDropOnInactiveList: Deactivating {modsToDeactivate.Count} mods.");
                _modListManager.DeactivateMods(modsToDeactivate); // Requires implementation in ModListManager
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
