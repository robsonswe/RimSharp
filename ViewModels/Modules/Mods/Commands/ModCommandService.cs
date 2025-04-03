using RimSharp.Models;
using RimSharp.ViewModels.Modules.Mods.Management;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace RimSharp.ViewModels.Modules.Mods.Commands
{
    public class ModCommandService : IModCommandService
    {
        private readonly IModListManager _modListManager;

        public ModCommandService(IModListManager modListManager)
        {
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
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
            var result = MessageBox.Show(
                "This will remove all non-Core and non-Expansion mods from the active list.\nAre you sure?",
                "Confirm Clear", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
                
            if (result != MessageBoxResult.Yes) return;

            await Task.Run(() => _modListManager.ClearActiveList());
        }

        public async Task SortActiveModsAsync()
        {
            bool orderChanged = await Task.Run(() => _modListManager.SortActiveList());

            MessageBox.Show(
                orderChanged 
                    ? "Active mods sorted based on defined rules." 
                    : "Mods are already correctly sorted or a sorting error occurred (check logs for cycles).",
                "Sort Complete", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
    }
}