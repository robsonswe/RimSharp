using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Infrastructure.Mods.Sorting;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Services.Mangement
{
    public class ModListManager : IModListManager
    {
        private readonly ModStateTracker _stateTracker;
        private readonly ModOrderService _orderService;
        private readonly ModDependencySorter _dependencySorter;
        private readonly ModLookupService _lookupService;
        private readonly List<ModItem> _allAvailableMods = new();

        public event EventHandler ListChanged;

        public ModListManager()
        {
            _stateTracker = new ModStateTracker();
            _orderService = new ModOrderService();
            _dependencySorter = new ModDependencySorter();
            _lookupService = new ModLookupService();
        }

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _orderService.VirtualActiveMods;
        public IReadOnlyList<ModItem> AllInactiveMods => _stateTracker.AllInactiveMods;
       // public IReadOnlyList<string> MissingModIds => _stateTracker.MissingModIds; // Assuming this was needed elsewhere

        public void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds)
        {
            if (allAvailableMods == null) throw new ArgumentNullException(nameof(allAvailableMods));

            _allAvailableMods.Clear();
            _allAvailableMods.AddRange(allAvailableMods);
            _lookupService.Initialize(_allAvailableMods);

            var activeIdList = activeModPackageIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id.ToLowerInvariant())
                .ToList()
                ?? new List<string>();

             // Let StateTracker determine initial active/inactive states and missing IDs
            _stateTracker.Initialize(_allAvailableMods, _lookupService.GetLookupDictionary(), activeIdList);

             // Build initial active mods list for OrderService based on StateTracker's results
             var initialActiveModsForOrder = new List<(ModItem Mod, int LoadOrder)>();
             for (int index = 0; index < activeIdList.Count; index++)
             {
                 string packageId = activeIdList[index];
                 // Use TryGetMod which uses the lookup initialized above
                 if (_lookupService.TryGetMod(packageId, out var mod) && _stateTracker.IsModActive(mod)) // Ensure it's marked active by tracker
                 {
                     initialActiveModsForOrder.Add((Mod: mod, LoadOrder: index));
                 }
             }

            _orderService.Initialize(initialActiveModsForOrder);


            RaiseListChanged();
            Debug.WriteLine($"ModListManager Initialized: {_orderService.VirtualActiveMods.Count} active, {_stateTracker.AllInactiveMods.Count} inactive."); // Removed missing count reference
        }

        // --- Single Item Methods (delegate to bulk methods where appropriate for consistency) ---
        public void ActivateMod(ModItem mod) => ActivateModsAt(new[] { mod }, _orderService.VirtualActiveMods.Count);

        public void ActivateModAt(ModItem mod, int index) => ActivateModsAt(new[] { mod }, index);

        public void DeactivateMod(ModItem mod) => DeactivateMods(new[] { mod });

        public void ReorderMod(ModItem mod, int newIndex) => ReorderMods(new[] { mod }, newIndex);


        // --- Existing Methods (Modified slightly if needed) ---
        public void ClearActiveList()
        {
            var modsToKeep = _orderService.VirtualActiveMods // Use OrderService's view
                .Where(x => x.Mod != null && (x.Mod.ModType == ModType.Core || x.Mod.ModType == ModType.Expansion))
                .Select(x => x.Mod)
                .ToList();

            // Get mods to deactivate (all active except those to keep)
            var modsToDeactivate = _orderService.VirtualActiveMods
                                     .Select(x => x.Mod)
                                     .Except(modsToKeep)
                                     .ToList();

            // Deactivate using the bulk method
            DeactivateMods(modsToDeactivate); // This will update both state tracker and order service

            // Note: DeactivateMods already handles raising ListChanged if changes occurred.
            Debug.WriteLine($"Cleared active list, kept {modsToKeep.Count} core/expansion mods.");
        }


        public bool SortActiveList()
        {
            var currentActiveMods = _orderService.VirtualActiveMods.Select(x => x.Mod).ToList();
            if (currentActiveMods.Count <= 1) return false;

            var originalOrderIds = currentActiveMods
                .Select(mod => mod?.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            // Identify mods with explicit load before/after requirements (already implemented logic)
            var hasExplicitLoadBefore = new HashSet<ModItem>();
            var hasExplicitForceLoadBefore = new HashSet<ModItem>();
            foreach (var mod in currentActiveMods)
            {
                 if(mod == null) continue; // Safety check
                 if (mod.LoadBefore?.Count > 0 || mod.ForceLoadBefore?.Count > 0) hasExplicitLoadBefore.Add(mod);
                 if (mod.ForceLoadBefore?.Count > 0) hasExplicitForceLoadBefore.Add(mod);
                 // Consider LoadAfter/ForceLoadAfter as well if your sorter uses them
            }


            // Perform topological sort
            var sortedMods = _dependencySorter.TopologicalSort(currentActiveMods, hasExplicitLoadBefore, hasExplicitForceLoadBefore);
            if (sortedMods == null || sortedMods.Count != currentActiveMods.Count)
            {
                 Debug.WriteLine("Sorting failed or resulted in a different number of mods (cycle?).");
                return false; // Sort failed or removed items (shouldn't happen in pure sort)
            }

            var newOrderIds = sortedMods
                .Select(mod => mod?.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);

            if (orderChanged)
            {
                // Rebuild the order service list completely based on the sort result
                _orderService.Clear();
                _orderService.AddModsAt(sortedMods, 0); // Add all sorted mods starting at index 0

                RaiseListChanged(); // Raise event only if order actually changed
                Debug.WriteLine("Active mod list sorted successfully. Order changed.");
            }
            else
            {
                Debug.WriteLine("Sorting completed. Mod order sequence unchanged.");
            }

            return orderChanged;
        }

        public bool IsModActive(ModItem mod) => _stateTracker.IsModActive(mod);

        public IEnumerable<ModItem> GetAllMods() => _allAvailableMods;

        private void RaiseListChanged()
        {
             Debug.WriteLine("ModListManager: Raising ListChanged event.");
            ListChanged?.Invoke(this, EventArgs.Empty);
        }


        // --- IMPLEMENTATION OF NEW BULK METHODS ---

        public void ActivateModsAt(IEnumerable<ModItem> mods, int index)
        {
            if (mods == null) return;
            bool listChanged = false;

            var modsToProcess = mods.Where(m => m != null).Distinct().ToList();
            if (!modsToProcess.Any()) return;

            Debug.WriteLine($"ModListManager: ActivateModsAt called for {modsToProcess.Count} mods at index {index}.");

            // Separate into mods to newly activate vs mods already active (potential reorder)
            var modsToActivate = modsToProcess.Where(m => !_stateTracker.IsModActive(m)).ToList();
            var modsToReorder = modsToProcess.Where(m => _stateTracker.IsModActive(m)).ToList();

            // --- Handle Reordering First ---
            if (modsToReorder.Any())
            {
                // We need to move these existing active mods towards the target index.
                // Call the specific ReorderMods method which handles index calculations correctly.
                 Debug.WriteLine($"ActivateModsAt: Reordering {modsToReorder.Count} already active mods.");
                 // Note: ReorderMods assumes all items in its list *are* active.
                 bool reorderChanged = _orderService.ReorderMods(modsToReorder, index);
                 listChanged |= reorderChanged;
                 // Adjust the insertion index for *new* mods based on where the reordered block ended up.
                 // Find the index of the last item from the reordered block.
                 var lastReorderedMod = modsToReorder.LastOrDefault(m => _orderService.VirtualActiveMods.Any(vm => vm.Mod == m)); // Find last one actually present after reorder
                 if(lastReorderedMod != null)
                 {
                     index = _orderService.VirtualActiveMods.ToList().FindIndex(vm => vm.Mod == lastReorderedMod) + 1;
                 }
                 // Ensure index is still valid after potential reordering
                 index = Math.Clamp(index, 0, _orderService.VirtualActiveMods.Count);
            }

            // --- Handle Activation of New Mods ---
            if (modsToActivate.Any())
            {
                Debug.WriteLine($"ActivateModsAt: Activating {modsToActivate.Count} new mods.");
                // Activate in state tracker
                foreach (var mod in modsToActivate)
                {
                    _stateTracker.Activate(mod); // Updates inactive list and active set
                }

                // Add to order service at the (potentially adjusted) index
                bool added = _orderService.AddModsAt(modsToActivate, index);
                listChanged |= added;
            }

             if (listChanged)
             {
                 RaiseListChanged();
             }
             else
             {
                  Debug.WriteLine($"ModListManager: ActivateModsAt resulted in no changes.");
             }
        }


        public void DeactivateMods(IEnumerable<ModItem> mods)
        {
            if (mods == null) return;
            bool listChanged = false;

            // Filter out nulls, core mods, and ensure distinctness
            var modsToDeactivate = mods
                .Where(m => m != null && m.ModType != ModType.Core) // Do not allow Core deactivation
                .Distinct()
                .ToList();

            if (!modsToDeactivate.Any())
            {
                 Debug.WriteLine($"ModListManager: No valid mods to deactivate.");
                return;
            }

            Debug.WriteLine($"ModListManager: DeactivateMods called for {modsToDeactivate.Count} mods.");

            // Deactivate in state tracker first
            foreach (var mod in modsToDeactivate)
            {
                _stateTracker.Deactivate(mod); // Updates active set and adds to inactive list
            }

            // Remove from order service
            bool removed = _orderService.RemoveMods(modsToDeactivate);
            listChanged = removed; // If OrderService removed something, the list changed

            if (listChanged)
            {
                RaiseListChanged();
            }
             else
             {
                  Debug.WriteLine($"ModListManager: DeactivateMods resulted in no changes.");
             }
        }


        public void ReorderMods(IEnumerable<ModItem> modsToMove, int targetIndex)
        {
            if (modsToMove == null) return;

            // Ensure all mods are actually active and not null/core (core shouldn't be manually reordered usually)
            var validModsToMove = modsToMove
                .Where(m => m != null && _stateTracker.IsModActive(m) /* && m.ModType != ModType.Core */ ) // Allow reordering core if needed? Decide based on requirements.
                .Distinct()
                .ToList();

            if (!validModsToMove.Any())
            {
                 Debug.WriteLine($"ModListManager: No valid active mods provided for ReorderMods.");
                return;
            }

            Debug.WriteLine($"ModListManager: ReorderMods called for {validModsToMove.Count} mods to target index {targetIndex}.");

            // Delegate directly to the order service's bulk reorder method
            bool changed = _orderService.ReorderMods(validModsToMove, targetIndex);

            if (changed)
            {
                RaiseListChanged();
            }
             else
             {
                  Debug.WriteLine($"ModListManager: ReorderMods resulted in no change in order.");
             }
        }

        // --- END IMPLEMENTATION OF NEW BULK METHODS ---


        // ResolveDependencies remains the same as provided in the previous turn
        public (List<ModItem> addedMods, List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)> missingDependencies) ResolveDependencies()
        {
            var addedMods = new List<ModItem>();
            var missingDependencies = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)>();
            bool listChanged = false;

            // Use OrderService view to get current active mods
            var activeModsForCheck = _orderService.VirtualActiveMods
                .Select(x => x.Mod)
                // .Where(m => m.ModType != ModType.Core && m.ModType != ModType.Expansion) // Keep this filtering? Dependencies might be needed even for core/exp
                .ToList();

            // Use StateTracker view for inactive mods
            var inactiveModsLookup = _stateTracker.AllInactiveMods.ToDictionary(m => m.PackageId, m => m, StringComparer.OrdinalIgnoreCase);

            // Use a HashSet for efficient checking of already active/processed dependencies
            var activePackageIds = new HashSet<string>(_orderService.GetActiveModIds(), StringComparer.OrdinalIgnoreCase);

            var modsToCheckQueue = new Queue<ModItem>(activeModsForCheck);
            var processedDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Track dependencies we've looked for

             // Dictionary to aggregate missing dependencies and their requesters
             var missingDepsDict = new Dictionary<string, (string displayName, string steamUrl, List<string> requiredBy)>(StringComparer.OrdinalIgnoreCase);


            while (modsToCheckQueue.Count > 0)
            {
                var currentMod = modsToCheckQueue.Dequeue();
                if (currentMod?.ModDependencies == null) continue;

                foreach (var dependency in currentMod.ModDependencies)
                {
                    var depPackageIdLower = dependency.PackageId.ToLowerInvariant();

                    // Skip if already active or already processed as a dependency
                    if (activePackageIds.Contains(depPackageIdLower) || processedDependencies.Contains(depPackageIdLower))
                        continue;

                    processedDependencies.Add(depPackageIdLower); // Mark this dependency as processed

                    // Try find it in the inactive list
                    if (inactiveModsLookup.TryGetValue(depPackageIdLower, out var dependencyMod))
                    {
                        Debug.WriteLine($"ResolveDependencies: Found inactive dependency '{dependencyMod.Name}' ({depPackageIdLower}) required by '{currentMod.Name}'. Activating.");
                        // Activate it (using single-item method is fine here as it's iterative)
                         _stateTracker.Activate(dependencyMod);
                         _orderService.AddMod(dependencyMod, _orderService.VirtualActiveMods.Count); // Add to end for now, sort later

                        addedMods.Add(dependencyMod);
                        activePackageIds.Add(depPackageIdLower); // Add to active set for subsequent checks
                        modsToCheckQueue.Enqueue(dependencyMod); // Check dependencies of the newly added mod
                        listChanged = true;
                    }
                    else
                    {
                        // Dependency is missing entirely
                         Debug.WriteLine($"ResolveDependencies: Missing dependency '{dependency.DisplayName}' ({depPackageIdLower}) required by '{currentMod.Name}'.");
                         if (missingDepsDict.TryGetValue(depPackageIdLower, out var existingMissing))
                         {
                             existingMissing.requiredBy.Add(currentMod.Name);
                         }
                         else
                         {
                             missingDepsDict[depPackageIdLower] = (
                                 displayName: dependency.DisplayName ?? depPackageIdLower,
                                 steamUrl: dependency.SteamWorkshopUrl,
                                 requiredBy: new List<string> { currentMod.Name ?? "Unknown Mod" }
                             );
                         }
                    }
                }
            }

            // Convert dictionary to list format for return
             missingDependencies = missingDepsDict.Select(kvp => (
                 kvp.Value.displayName,
                 kvp.Key, // packageId
                 kvp.Value.steamUrl,
                 kvp.Value.requiredBy
             )).ToList();


            if (listChanged)
            {
                // Optional: Re-sort if dependencies were added?
                // SortActiveList(); // Uncomment if adding dependencies should trigger immediate resort
                RaiseListChanged();
            }

            return (addedMods, missingDependencies);
        }
    }
}
