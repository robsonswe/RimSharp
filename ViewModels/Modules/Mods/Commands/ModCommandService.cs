using RimSharp.Models;
using RimSharp.ViewModels.Modules.Mods.Management;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace RimSharp.ViewModels.Modules.Mods.Commands
{
    public class ModCommandService : IModCommandService
    {
        private readonly IModListManager _modListManager;

        public ModCommandService(IModListManager modListManager)
        {
            _modListManager = modListManager;
        }

        public Task HandleDropCommand(DropModArgs args)
        {
            if (args?.DroppedItem == null || string.IsNullOrEmpty(args.TargetListName)) 
                return Task.CompletedTask;

            ModItem draggedMod = args.DroppedItem;
            int dropIndex = args.DropIndex;
            string targetList = args.TargetListName;
            
            Debug.WriteLine($"Drop executed: Item '{draggedMod.Name}', Target: {targetList}, Index: {dropIndex}");

            if (targetList.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                bool isCurrentlyActive = _modListManager.IsModActive(draggedMod);
                
                if (isCurrentlyActive)
                {
                    Debug.WriteLine($"Reordering active mod '{draggedMod.Name}' to index {dropIndex}");
                    _modListManager.ReorderMod(draggedMod, dropIndex);
                }
                else
                {
                    Debug.WriteLine($"Adding inactive mod '{draggedMod.Name}' to active list at index {dropIndex}");
                    _modListManager.ActivateModAt(draggedMod, dropIndex);
                }
            }
            else if (targetList.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            {
                bool isCurrentlyActive = _modListManager.IsModActive(draggedMod);
                
                if (isCurrentlyActive)
                {
                    Debug.WriteLine($"Removing active mod '{draggedMod.Name}' (dropped onto inactive)");
                    _modListManager.DeactivateMod(draggedMod);
                }
                else
                {
                    Debug.WriteLine($"Mod '{draggedMod.Name}' dropped onto inactive list, but it was already inactive. No action taken.");
                }
            }
            else 
            { 
                Debug.WriteLine($"Drop target list name '{targetList}' not recognized."); 
            }

            return Task.CompletedTask;
        }

        public async Task ClearActiveModsAsync()
        {
            var result = MessageBox.Show("This will remove all non-Core and non-Expansion mods from the active list.\nAre you sure?",
                                    "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            await Task.Run(() => _modListManager.ClearActiveList());
        }

        public async Task SortActiveModsAsync()
        {
            bool orderChanged = await Task.Run(() => _modListManager.SortActiveList());

            if (orderChanged)
            {
                MessageBox.Show("Active mods sorted based on defined rules.", "Sort Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Mods are already correctly sorted or a sorting error occurred (check logs for cycles).",
                               "Sort Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
