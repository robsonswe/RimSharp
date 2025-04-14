using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Services.Management
{
    public class ModOrderService
    {
        // Use a list of the actual ModItem for easier manipulation before final tuple creation
        private readonly List<ModItem> _orderedActiveMods = new();

        // The public property converts the internal list to the required tuple format on demand
        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods =>
             _orderedActiveMods.Select((mod, index) => (Mod: mod, LoadOrder: index)).ToList();

        public void Initialize(IEnumerable<(ModItem Mod, int LoadOrder)> initialMods)
        {
            _orderedActiveMods.Clear();
            // Order the initial mods by their LoadOrder and add just the ModItem
            _orderedActiveMods.AddRange(initialMods.OrderBy(m => m.LoadOrder).Select(m => m.Mod));
            Debug.WriteLine($"ModOrderService Initialized with {_orderedActiveMods.Count} mods.");
        }

        public void AddMod(ModItem mod, int index)
        {
            if (mod == null || _orderedActiveMods.Contains(mod)) // Prevent duplicates
            {
                Debug.WriteLineIf(mod != null && _orderedActiveMods.Contains(mod), $"ModOrderService: Mod '{mod.Name}' already in the active list. AddMod skipped.");
                return;
            }
            index = Math.Clamp(index, 0, _orderedActiveMods.Count);
            _orderedActiveMods.Insert(index, mod);
            Debug.WriteLine($"ModOrderService: Added mod '{mod?.Name}' at index {index}. New count: {_orderedActiveMods.Count}");
        }

        public void RemoveMod(ModItem mod)
        {
            if (mod == null) return;
            bool removed = _orderedActiveMods.Remove(mod);
            if(removed)
            {
                Debug.WriteLine($"ModOrderService: Removed mod '{mod.Name}'. New count: {_orderedActiveMods.Count}");
            }
            else
            {
                Debug.WriteLine($"ModOrderService: Mod '{mod.Name}' not found for removal.");
            }
        }

        public void ReorderMod(ModItem mod, int newIndex)
        {
             if (mod == null) return;
             int currentIndex = _orderedActiveMods.IndexOf(mod);
             if (currentIndex == -1)
             {
                 Debug.WriteLine($"ModOrderService: Mod '{mod.Name}' not found for reordering.");
                 return;
             }

            // Use the bulk reorder logic for simplicity and consistency
            ReorderMods(new List<ModItem> { mod }, newIndex);
        }

        public void Clear()
        {
            _orderedActiveMods.Clear();
            Debug.WriteLine("ModOrderService: Cleared active mods.");
        }

        // --- ADD THESE NEW BULK METHODS ---

        /// <summary>
        /// Adds a collection of mods starting at the specified index.
        /// Assumes mods are not already present in the list. Duplicates are skipped.
        /// </summary>
        public bool AddModsAt(IEnumerable<ModItem> modsToAdd, int index)
        {
            if (modsToAdd == null) return false;

            var uniqueModsToAdd = modsToAdd.Where(m => m != null && !_orderedActiveMods.Contains(m)).ToList();
            if (!uniqueModsToAdd.Any())
            {
                 Debug.WriteLine($"ModOrderService: No new mods to add in AddModsAt.");
                return false;
            }

            index = Math.Clamp(index, 0, _orderedActiveMods.Count);
            _orderedActiveMods.InsertRange(index, uniqueModsToAdd);
            Debug.WriteLine($"ModOrderService: Added {uniqueModsToAdd.Count} mods at index {index}. New count: {_orderedActiveMods.Count}");
            return true; // Indicate that the list was changed
        }

        /// <summary>
        /// Removes a collection of mods from the ordered list.
        /// </summary>
        public bool RemoveMods(IEnumerable<ModItem> modsToRemove)
        {
            if (modsToRemove == null) return false;
            int originalCount = _orderedActiveMods.Count;
            int removedCount = _orderedActiveMods.RemoveAll(mod => modsToRemove.Contains(mod));

            if (removedCount > 0)
            {
                Debug.WriteLine($"ModOrderService: Removed {removedCount} mods. New count: {_orderedActiveMods.Count}");
                return true; // Indicate change
            }
             Debug.WriteLine($"ModOrderService: No mods removed in RemoveMods call.");
            return false; // Indicate no change
        }

        /// <summary>
        /// Reorders a specified list of (already active) mods to a target insertion index,
        /// maintaining their relative order.
        /// </summary>
        public bool ReorderMods(IEnumerable<ModItem> modsToMove, int targetIndex)
        {
            if (modsToMove == null) return false;

            var modsToMoveList = modsToMove.Distinct().ToList(); // Ensure unique items

            // Get current indices and validate all mods are actually present
            var currentEntries = modsToMoveList
                .Select(mod => new { Mod = mod, CurrentIndex = _orderedActiveMods.IndexOf(mod) })
                .Where(x => x.CurrentIndex != -1) // Filter out any mods not actually found
                .OrderBy(x => x.CurrentIndex)
                .ToList();

             // Check if all requested mods were found
            if (currentEntries.Count != modsToMoveList.Count)
            {
                 var notFound = modsToMoveList.Where(m => !currentEntries.Any(ce => ce.Mod == m)).Select(m=>m.Name);
                 Debug.WriteLine($"ModOrderService: ReorderMods failed. Could not find the following mods in the active list: {string.Join(", ", notFound)}");
                return false;
            }

            if (!currentEntries.Any())
            {
                 Debug.WriteLine($"ModOrderService: No valid mods to reorder.");
                return false; // Nothing to move
            }


            // --- Index Calculation & Movement ---
             // Store the actual ModItem objects being moved, in their original relative order.
            var movingModItems = currentEntries.Select(x => x.Mod).ToList();

            // Calculate the effective target index *after* simulated removal.
            // Count how many of the items being moved appear *before* the original targetIndex.
            int itemsBeforeTarget = currentEntries.Count(x => x.CurrentIndex < targetIndex);
            int actualInsertionIndex = Math.Clamp(targetIndex - itemsBeforeTarget, 0, _orderedActiveMods.Count - currentEntries.Count);

             // Perform removal. Easiest way is RemoveAll with the list of mods to move.
             int removedCount = _orderedActiveMods.RemoveAll(mod => movingModItems.Contains(mod));
             if(removedCount != movingModItems.Count)
             {
                  // This shouldn't happen if the initial check passed, but good safeguard.
                  Debug.WriteLine($"ModOrderService: ReorderMods - Mismatch during removal! Expected {movingModItems.Count}, removed {removedCount}. Aborting reorder.");
                  // Consider restoring the list state if possible, or log error prominently.
                  return false;
             }


             // Perform insertion at the calculated index.
             _orderedActiveMods.InsertRange(actualInsertionIndex, movingModItems);
            // --- End Index Calculation & Movement ---

            Debug.WriteLine($"ModOrderService: Reordered {movingModItems.Count} mods to start at effective index {actualInsertionIndex}.");
            return true; // Indicate change
        }


        // --- END ADDED BULK METHODS ---

        // Replaced by the on-demand calculation in the VirtualActiveMods property
        // public void ReIndexVirtualActiveMods() { }

        public IEnumerable<string> GetActiveModIds()
        {
            // Now reads from the internal list
            return _orderedActiveMods
                .Select(m => m?.PackageId?.ToLowerInvariant())
                .Where(id => !string.IsNullOrEmpty(id));
        }
    }
}
