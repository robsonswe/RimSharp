using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModListManager : IModListManager
    {
        private List<(ModItem Mod, int LoadOrder)> _virtualActiveMods = new();
        private List<ModItem> _allInactiveMods = new();
        private List<ModItem> _allAvailableMods = new(); // Keep original reference

        // --- Optimization Fields ---
        // Lookup for all available mods by lowercase packageId for O(1) average access
        private Dictionary<string, ModItem> _modLookup = new(StringComparer.OrdinalIgnoreCase);
        // Set of currently active mods for O(1) average containment checks
        private HashSet<ModItem> _activeModSet = new();
        // --------------------------

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _virtualActiveMods.AsReadOnly();
        public IReadOnlyList<ModItem> AllInactiveMods => _allInactiveMods.AsReadOnly(); // Sort happens only when list changes significantly

        public event EventHandler ListChanged;

        public void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds)
        {
            _allAvailableMods = allAvailableMods?.ToList() ?? new List<ModItem>();
            var activeIdList = activeModPackageIds?.Select(id => id?.ToLowerInvariant()) // Ensure lowercase and handle potential nulls
                                                .Where(id => id != null)
                                                .ToList() ?? new List<string>();

            // --- Optimization: Build Lookup First ---
            _modLookup = _allAvailableMods
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase) // Handle potential duplicates gracefully (take first)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            // ---------------------------------------

            // Build virtual active mods list with load order based on config
            var activeModsFromConfig = new List<(ModItem Mod, int LoadOrder)>();
            _activeModSet.Clear(); // Clear previous state

            for (int index = 0; index < activeIdList.Count; index++)
            {
                string packageId = activeIdList[index];
                // --- Optimization: Use Lookup ---
                if (_modLookup.TryGetValue(packageId, out var mod))
                // ---------------------------------
                {
                    activeModsFromConfig.Add((Mod: mod, LoadOrder: index));
                    _activeModSet.Add(mod); // Add to the fast lookup set
                }
                else
                {
                     Debug.WriteLine($"Warning: Active mod ID '{packageId}' from config not found in available mods.");
                }
            }
            _virtualActiveMods = activeModsFromConfig;


            // Set IsActive flag on active mods (redundant if ModItem state is reliable, but safe)
            foreach (var (mod, _) in _virtualActiveMods)
            {
                mod.IsActive = true;
            }

            // Determine inactive mods
            // --- Optimization: Use HashSet for faster check ---
            _allInactiveMods = _allAvailableMods
                .Where(mod => !_activeModSet.Contains(mod))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase) // Initial sort
                .ToList();
            // ------------------------------------------------

            // Ensure inactive mods have IsActive = false
            foreach (var inactiveMod in _allInactiveMods)
            {
                inactiveMod.IsActive = false;
            }

            RaiseListChanged(); // Notify listeners of the initial state
            Debug.WriteLine($"ModListManager Initialized: {_virtualActiveMods.Count} active, {_allInactiveMods.Count} inactive.");
        }

        public void ActivateMod(ModItem mod)
        {
            ActivateModAt(mod, _virtualActiveMods.Count); // Add to end
        }

        public void ActivateModAt(ModItem mod, int index)
        {
            if (mod == null) return;

            // --- Optimization: Use HashSet ---
            if (_activeModSet.Contains(mod))
            // ---------------------------------
            {
                Debug.WriteLine($"ActivateModAt: Mod '{mod.Name}' is already active.");
                return;
            }

            // Remove from inactive list (O(n), unavoidable with List unless index known)
            bool removedFromInactive = _allInactiveMods.Remove(mod);
            if (!removedFromInactive)
            {
                // This might happen if the mod wasn't in the initial _allAvailableMods or state is inconsistent
                Debug.WriteLine($"Warning: Mod '{mod.Name}' activated but wasn't found in the internal inactive list.");
            }

            index = Math.Clamp(index, 0, _virtualActiveMods.Count);

            _virtualActiveMods.Insert(index, (mod, -1)); // Insert with temp load order (O(n))
            _activeModSet.Add(mod); // Add to set (O(1))
            mod.IsActive = true;

            ReIndexVirtualActiveMods(); // Assign proper sequential load orders (O(n))
            RaiseListChanged();
            Debug.WriteLine($"Activated mod '{mod.Name}' at index {index}.");
        }

        public void DeactivateMod(ModItem mod)
        {
            if (mod == null) return;

            if (mod.IsCore)
            {
                Debug.WriteLine($"Attempt blocked: Cannot deactivate the Core mod '{mod.Name}'.");
                return;
            }

            // FindIndex is O(n), RemoveAt is O(n)
            var itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex != -1)
            {
                _virtualActiveMods.RemoveAt(itemIndex);
                bool removedFromSet = _activeModSet.Remove(mod); // O(1)
                if (!removedFromSet) Debug.WriteLine($"Warning: Deactivated mod '{mod.Name}' was not found in the active set tracker.");
                mod.IsActive = false;

                // Add to inactive list (O(1) amortized), but don't sort here.
                // Sorting the whole inactive list on every deactivation is inefficient.
                // We sort it initially and when clearing the list. If UI needs sorted inactive, FilterService handles it.
                 if (!_allInactiveMods.Contains(mod)) // O(n) check, maybe accept duplicates and filter later if needed? Or use a HashSet for inactive check too? Let's keep it simple for now.
                 {
                      _allInactiveMods.Add(mod);
                      // --- Optimization: Removed Sort ---
                      // _allInactiveMods.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                 }

                ReIndexVirtualActiveMods(); // Re-index remaining active mods (O(n))
                RaiseListChanged();
                Debug.WriteLine($"Deactivated mod '{mod.Name}'.");
            }
            else
            {
                // --- Optimization: Use HashSet for faster check if it *should* be active ---
                if (_activeModSet.Contains(mod))
                {
                     Debug.WriteLine($"Critical Warning: Mod '{mod.Name}' found in active set but not in virtual list! State inconsistency.");
                     // Attempt recovery? Or just log. For now, log.
                     _activeModSet.Remove(mod); // Try to correct the set
                }
                // -----------------------------------------------------------------------------
                Debug.WriteLine($"DeactivateMod: Mod '{mod.Name}' not found in virtual active list.");
            }
        }

        public void ReorderMod(ModItem mod, int newIndex)
        {
            if (mod == null) return;

            // FindIndex is O(n)
            var currentItemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (currentItemIndex == -1)
            {
                Debug.WriteLine($"Reorder error: Mod '{mod.Name}' not found in virtual active list.");
                return;
            }

            // Clamp target index
            // Note: The original calculation for targetInsertIndex had potential issues when moving
            // items downwards. Clamping before adjusting for removal is safer.
            int targetClampedIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count);

            // Check if the *effective* position is the same.
            // E.g. Moving item at index 2 to index 3 is a no-op after removal adjustment.
            int effectiveTargetIndex = (targetClampedIndex > currentItemIndex) ? targetClampedIndex - 1 : targetClampedIndex;
            effectiveTargetIndex = Math.Clamp(effectiveTargetIndex, 0, _virtualActiveMods.Count -1); //Clamp against new size

            if (effectiveTargetIndex == currentItemIndex) {
                 Debug.WriteLine($"Reorder request for '{mod.Name}' to index {newIndex} results in no change (current: {currentItemIndex}).");
                 // Even if no move, re-index might be needed if target index was out of bounds initially?
                 // No, if effective position is same, list content/order identical. Return early.
                 return;
            }


            Debug.WriteLine($"Reordering '{mod.Name}' from {currentItemIndex} to effective index {effectiveTargetIndex}");

            var itemToMove = _virtualActiveMods[currentItemIndex];
            _virtualActiveMods.RemoveAt(currentItemIndex); // O(n)
            _virtualActiveMods.Insert(effectiveTargetIndex, itemToMove); // O(n)

            ReIndexVirtualActiveMods(); // Re-assign all load orders (O(n))
            RaiseListChanged();
        }


        public void ClearActiveList()
        {
            // Identify mods to remove (not Core or Expansion)
            var modsToRemove = new List<ModItem>();
            var modsToKeep = new List<(ModItem Mod, int LoadOrder)>();

            // Iterate once to partition
            foreach (var entry in _virtualActiveMods)
            {
                if (!entry.Mod.IsCore && !entry.Mod.IsExpansion)
                {
                    modsToRemove.Add(entry.Mod);
                }
                else
                {
                    modsToKeep.Add(entry); // Keep original tuple to preserve relative order info briefly
                }
            }

            if (!modsToRemove.Any()) return; // Nothing to clear

            // Sort modsToKeep by their original LoadOrder to maintain relative order
             modsToKeep.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));

            // Update the virtual active list (rebuild)
            _virtualActiveMods = modsToKeep;

            // Update active set and inactive list
            _activeModSet.Clear(); // Faster to clear and rebuild
            foreach (var (mod, _) in _virtualActiveMods)
            {
                 _activeModSet.Add(mod); // Rebuild the set O(k) where k is kept mods
            }

            foreach (var mod in modsToRemove)
            {
                mod.IsActive = false;
                // Avoid adding duplicates if ClearActiveList is called multiple times or state is odd
                if (!_allInactiveMods.Contains(mod)) // O(n) check
                {
                     _allInactiveMods.Add(mod); // O(1) amortized
                }
            }

            // Re-sort the *entire* inactive list now that many items were added
            _allInactiveMods.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)); // O(m log m)

            ReIndexVirtualActiveMods(); // Re-index the remaining active mods (O(k))
            RaiseListChanged();
            Debug.WriteLine($"Cleared active list, removed {modsToRemove.Count} mods.");
        }


        public bool SortActiveList()
        {
            if (_virtualActiveMods.Count <= 1) return false;

            var originalOrderIds = _virtualActiveMods
                                    .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                                    .ToList();

            // Topological sort logic remains the same internally
            var sortedMods = TopologicalSortActiveModsWithPriority();

            if (sortedMods == null)
            {
                Debug.WriteLine("Sorting failed due to conflicting mod rules (cycle detected).");
                return false; // Indicate failure
            }

            // Rebuild the virtual active list with new order and indices
            _virtualActiveMods = sortedMods
                .Select((mod, index) => (Mod: mod, LoadOrder: index))
                .ToList();

            // ActiveSet doesn't need update as the *content* is the same, just order changed.

            var newOrderIds = _virtualActiveMods
                               .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                               .ToList();

            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);

            // Always raise ListChanged after sort to update UI with new LoadOrder numbers, even if sequence is same
            RaiseListChanged();

            if (orderChanged)
            {
                Debug.WriteLine("Active mod list sorted successfully. Order changed.");
                return true;
            }
            else
            {
                Debug.WriteLine("Sorting completed. Mod order sequence unchanged (but load order numbers refreshed).");
                // Return true because the list *was* processed and re-indexed, even if sequence didn't change.
                // Or return false if only sequence change matters? Let's return true as an action occurred.
                return true;
            }
        }


        // --- Private Helpers ---

        private void ReIndexVirtualActiveMods()
        {
            // O(n) operation where n is the number of active mods
            for (int i = 0; i < _virtualActiveMods.Count; i++)
            {
                _virtualActiveMods[i] = (_virtualActiveMods[i].Mod, i); // Assign sequential load order
            }
        }

        private void RaiseListChanged()
        {
            // Consider dispatching if called from non-UI thread, although currently seems UI-driven
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        // --- Topological Sort Logic (Internal - largely unchanged) ---
        private List<ModItem> TopologicalSortActiveModsWithPriority()
        {
             // Uses _virtualActiveMods as source. Builds its own lookup scoped to active mods.

            if (!_virtualActiveMods.Any()) return new List<ModItem>();

            var sortedResult = new List<ModItem>(_virtualActiveMods.Count);
            var activeModsOnly = _virtualActiveMods.Select(vm => vm.Mod).ToList();

            // Build PackageId lookup (lowercase keys for dependency resolution within active mods)
            // Use manager's lookup for potentially faster checks if needed, but this local one is fine.
            var localModLookup = activeModsOnly
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            var adj = new Dictionary<ModItem, HashSet<ModItem>>();
            var inDegree = new Dictionary<ModItem, int>();

            foreach (var mod in activeModsOnly)
            {
                adj[mod] = new HashSet<ModItem>();
                inDegree[mod] = 0;
            }

            // Populate graph based on rules
            foreach (var mod in activeModsOnly)
            {
                // string modPackageIdLower = mod.PackageId?.ToLowerInvariant(); // Already handled by localModLookup keys
                // if (string.IsNullOrEmpty(modPackageIdLower)) continue; // Handled by lookup check

                Action<IEnumerable<string>> processLoadAfter = ids =>
                {
                    foreach (var prereqId in ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id.ToLowerInvariant()).Distinct())
                    {
                        // Use local lookup for dependencies within the active set
                        if (localModLookup.TryGetValue(prereqId, out var prereqMod) && prereqMod != mod)
                        {
                            if (adj.TryGetValue(prereqMod, out var successors) && successors.Add(mod))
                            {
                                inDegree[mod]++;
                            }
                        }
                    }
                };

                Action<IEnumerable<string>> processLoadBefore = ids =>
                {
                    foreach (var successorId in ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id.ToLowerInvariant()).Distinct())
                    {
                         if (localModLookup.TryGetValue(successorId, out var successorMod) && successorMod != mod)
                        {
                            if (adj.TryGetValue(mod, out var successors) && successors.Add(successorMod))
                            {
                                inDegree[successorMod]++;
                            }
                        }
                    }
                };

                // Process rules using helpers
                processLoadAfter(mod.LoadAfter ?? Enumerable.Empty<string>());
                processLoadAfter(mod.ForceLoadAfter ?? Enumerable.Empty<string>());
                processLoadAfter((mod.ModDependencies ?? Enumerable.Empty<ModDependency>()).Select(d => d.PackageId));

                processLoadBefore(mod.LoadBefore ?? Enumerable.Empty<string>());
                processLoadBefore(mod.ForceLoadBefore ?? Enumerable.Empty<string>());
            }

            // --- Kahn's Algorithm with Priority Tie-breaking ---
            var readyNodes = new List<ModItem>(activeModsOnly.Where(m => inDegree[m] == 0));

            // Comparer prioritizes Core, then Expansions, then alphabetically by Name
             Comparison<ModItem> priorityComparer = (modA, modB) =>
             {
                 int coreCompare = modB.IsCore.CompareTo(modA.IsCore); // True (1) comes before False (0)
                 if (coreCompare != 0) return coreCompare;

                 int expansionCompare = modB.IsExpansion.CompareTo(modA.IsExpansion); // True before False
                 if (expansionCompare != 0) return expansionCompare;

                 // Fallback to name comparison
                 return StringComparer.OrdinalIgnoreCase.Compare(modA.Name, modB.Name);
             };


            while (readyNodes.Count > 0)
            {
                readyNodes.Sort(priorityComparer); // Sort ready nodes based on priority
                var current = readyNodes[0];
                readyNodes.RemoveAt(0);
                sortedResult.Add(current);

                // Use OrderBy for deterministic neighbor processing (helps consistency)
                var neighbors = adj.TryGetValue(current, out var successorSet)
                               ? successorSet.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList()
                               : new List<ModItem>();

                foreach (var neighbor in neighbors)
                {
                    if (inDegree.ContainsKey(neighbor))
                    {
                        inDegree[neighbor]--;
                        if (inDegree[neighbor] == 0)
                        {
                            readyNodes.Add(neighbor);
                        }
                        else if (inDegree[neighbor] < 0)
                        {
                             // This indicates a flaw in the algorithm or graph state.
                            Debug.WriteLine($"Critical Error: In-degree for {neighbor.Name} became negative during sort.");
                            // Cycle detection should catch the overall failure.
                        }
                    }
                    else {
                         Debug.WriteLine($"Warning: Neighbor {neighbor.Name} of {current.Name} not found in inDegree map during sort.");
                    }
                }
            }

            // Check for cycles
            if (sortedResult.Count != activeModsOnly.Count)
            {
                var cycleMods = activeModsOnly.Except(sortedResult).Select(m => m.Name);
                var remainingInDegrees = inDegree.Where(kvp => kvp.Value > 0).Select(kvp => $"{kvp.Key.Name}({kvp.Value})");
                Debug.WriteLine($"Cycle detected during sort. Mods likely involved: {string.Join(", ", cycleMods)}. Remaining dependencies: {string.Join(", ", remainingInDegrees)}");
                return null; // Indicate failure
            }

            Debug.WriteLine($"Priority topological sort successful."); // Final Order logged by caller if needed
            return sortedResult;
        }

        // --- Optimization: O(1) average check ---
        public bool IsModActive(ModItem mod)
        {
            // Check the hash set for fast lookup
            return mod != null && _activeModSet.Contains(mod);
        }
        // ----------------------------------------

        public IEnumerable<ModItem> GetAllMods()
        {
            // Returns the original list provided during initialization
            return _allAvailableMods;
        }

        // Added helper to get mod by package ID using the lookup
        public ModItem GetModByPackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            _modLookup.TryGetValue(packageId, out var mod);
            return mod;
        }

    }
}
