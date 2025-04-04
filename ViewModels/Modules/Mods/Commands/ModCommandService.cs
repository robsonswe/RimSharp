using RimSharp.Models;
using RimSharp.ViewModels.Modules.Mods.Management;
using System;
using System.Threading.Tasks;
using RimSharp.Services; // Added for IDialogService
using RimSharp.ViewModels.Dialogs;
using System.Diagnostics; // Added for MessageDialogResult

namespace RimSharp.ViewModels.Modules.Mods.Commands
{
    public class ModCommandService : IModCommandService
    {
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService; // Added

        // Updated Constructor
        public ModCommandService(IModListManager modListManager, IDialogService dialogService)
        {
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService)); // Added
        }

        public Task HandleDropCommand(DropModArgs args)
        {
            if (args?.DroppedItem == null || string.IsNullOrEmpty(args.TargetListName))
                return Task.CompletedTask;

            switch (args.TargetListName.ToLowerInvariant())
            {
                case "active":
                    HandleDropOnActiveList(args.DroppedItem, args.DropIndex);
                    break;

                case "inactive":
                    HandleDropOnInactiveList(args.DroppedItem);
                    break;

                default:
                    // Log unrecognized target list
                    break;
            }

            return Task.CompletedTask;
        }

        private void HandleDropOnActiveList(ModItem draggedMod, int dropIndex)
        {
            bool isCurrentlyActive = _modListManager.IsModActive(draggedMod);

            if (isCurrentlyActive)
            {
                // Reorder within active list
                _modListManager.ReorderMod(draggedMod, dropIndex);
            }
            else
            {
                // Add inactive mod to active list at specific position
                _modListManager.ActivateModAt(draggedMod, dropIndex);
            }
        }

        private void HandleDropOnInactiveList(ModItem draggedMod)
        {
            if (_modListManager.IsModActive(draggedMod))
            {
                // Remove from active list
                _modListManager.DeactivateMod(draggedMod);
            }
            // If already inactive, no action needed
        }

        public async Task ClearActiveModsAsync()
        {
            // --- Replaced MessageBox ---
            var result = _dialogService.ShowConfirmation(
                "Confirm Clear",
                "This will remove all non-Core and non-Expansion mods from the active list.\nAre you sure?",
                showCancel: true); // Show OK and Cancel buttons
                                   // -------------------------

            // Assuming OK maps to Yes, Cancel maps to No for this confirmation
            if (result != MessageDialogResult.OK) return; // Use MessageDialogResult.OK

            await Task.Run(() => _modListManager.ClearActiveList());
        }

        public async Task SortActiveModsAsync()
        {
            try
            {
                bool orderChanged = await Task.Run(() => _modListManager.SortActiveList());

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
