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
        private List<ModItem> _allAvailableMods = new(); // Keep a reference if needed

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _virtualActiveMods.AsReadOnly();
        public IReadOnlyList<ModItem> AllInactiveMods => _allInactiveMods.AsReadOnly();

        public event EventHandler ListChanged;

        public void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds)
        {
            _allAvailableMods = allAvailableMods?.ToList() ?? new List<ModItem>();
            var activeIdList = activeModPackageIds?.Select(id => id.ToLowerInvariant()).ToList() ?? new List<string>();

            // Build virtual active mods list with load order based on config
            _virtualActiveMods = activeIdList
                .Select((packageId, index) =>
                {
                    var mod = _allAvailableMods.FirstOrDefault(m =>
                        m.PackageId?.Equals(packageId, StringComparison.OrdinalIgnoreCase) == true);
                    return mod != null ? (Mod: mod, LoadOrder: index) : default;
                })
                .Where(entry => entry.Mod != null) // Filter out any mods not found
                .ToList();

            // Set IsActive flag and create a set for quick lookup
            var activeModsSet = new HashSet<ModItem>();
            foreach (var (mod, _) in _virtualActiveMods)
            {
                mod.IsActive = true;
                activeModsSet.Add(mod);
            }

            // Determine inactive mods
            _allInactiveMods = _allAvailableMods
                .Except(activeModsSet)
                .OrderBy(m => m.Name)
                .ToList();

            // Ensure inactive mods have IsActive = false
            foreach(var inactiveMod in _allInactiveMods)
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
            if (mod == null || _virtualActiveMods.Any(x => x.Mod == mod))
            {
                Debug.WriteLineIf(mod != null, $"ActivateModAt: Mod '{mod.Name}' is null or already active.");
                return;
            }

            bool removedFromInactive = _allInactiveMods.Remove(mod);
            if (!removedFromInactive)
            {
                Debug.WriteLine($"Warning: Mod '{mod.Name}' activated but wasn't found in the internal inactive list.");
            }

            index = Math.Clamp(index, 0, _virtualActiveMods.Count);

            _virtualActiveMods.Insert(index, (mod, -1)); // Insert with temp load order
            mod.IsActive = true;

            ReIndexVirtualActiveMods(); // Assign proper sequential load orders
            RaiseListChanged();
            Debug.WriteLine($"Activated mod '{mod.Name}' at index {index}.");
        }

        public void DeactivateMod(ModItem mod)
        {
            if (mod == null) return;

            if (mod.IsCore)
            {
                Debug.WriteLine($"Attempt blocked: Cannot deactivate the Core mod '{mod.Name}'.");
                // Optionally raise an event or return a status if the VM needs to show a message
                return;
            }

            var itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex != -1)
            {
                _virtualActiveMods.RemoveAt(itemIndex);
                mod.IsActive = false;

                if (!_allInactiveMods.Contains(mod))
                {
                    _allInactiveMods.Add(mod);
                    _allInactiveMods.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)); // Keep sorted
                }

                ReIndexVirtualActiveMods(); // Re-index remaining active mods
                RaiseListChanged();
                Debug.WriteLine($"Deactivated mod '{mod.Name}'.");
            }
            else
            {
                Debug.WriteLine($"DeactivateMod: Mod '{mod.Name}' not found in virtual active list.");
            }
        }

        public void ReorderMod(ModItem mod, int newIndex)
        {
             if (mod == null) return;

            var currentItemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (currentItemIndex == -1)
            {
                Debug.WriteLine($"Reorder error: Mod '{mod.Name}' not found in virtual active list.");
                return;
            }

            int targetInsertIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count);

            // Simple check for no actual move needed
            if (targetInsertIndex == currentItemIndex || (targetInsertIndex == currentItemIndex + 1 && targetInsertIndex > currentItemIndex))
            {
                 Debug.WriteLine($"Reorder request for '{mod.Name}' to index {newIndex} results in no change (current: {currentItemIndex}).");
                 return;
            }

            Debug.WriteLine($"Reordering '{mod.Name}' from {currentItemIndex} to insert at {targetInsertIndex}");

            var itemToMove = _virtualActiveMods[currentItemIndex];
            _virtualActiveMods.RemoveAt(currentItemIndex);

            // Adjust insertion index based on removal
            int actualInsertIndex = (targetInsertIndex > currentItemIndex) ? targetInsertIndex - 1 : targetInsertIndex;
            actualInsertIndex = Math.Clamp(actualInsertIndex, 0, _virtualActiveMods.Count);

            _virtualActiveMods.Insert(actualInsertIndex, itemToMove);

            ReIndexVirtualActiveMods(); // Re-assign all load orders
            RaiseListChanged();
        }

        public void ClearActiveList()
        {
             // Identify mods to remove (not Core or Expansion)
            var modsToRemove = _virtualActiveMods
                .Where(entry => !entry.Mod.IsCore && !entry.Mod.IsExpansion)
                .Select(entry => entry.Mod)
                .ToList();

            if (!modsToRemove.Any()) return; // Nothing to clear

            // Identify essential mods to keep and maintain relative order
            var modsToKeep = _virtualActiveMods
                .Where(entry => entry.Mod.IsCore || entry.Mod.IsExpansion)
                .OrderBy(entry => entry.LoadOrder) // Maintain original relative order
                .ToList(); // Keep tuples for now

            // Update the virtual active list
            _virtualActiveMods = modsToKeep;

            // Add removed mods to inactive list
            foreach (var mod in modsToRemove)
            {
                mod.IsActive = false;
                if (!_allInactiveMods.Contains(mod))
                {
                    _allInactiveMods.Add(mod);
                }
            }
            _allInactiveMods.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)); // Re-sort inactive

            ReIndexVirtualActiveMods(); // Re-index the remaining active mods
            RaiseListChanged();
            Debug.WriteLine($"Cleared active list, removed {modsToRemove.Count} mods.");
        }


       public bool SortActiveList()
        {
            if (_virtualActiveMods.Count <= 1) return false;

            var originalOrderIds = _virtualActiveMods
                                    .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                                    .ToList();

            var sortedMods = TopologicalSortActiveModsWithPriority();

            if (sortedMods == null)
            {
                Debug.WriteLine("Sorting failed due to conflicting mod rules (cycle detected).");
                // Let the VM handle showing the MessageBox
                return false; // Indicate failure or no change
            }

            var newVirtualActiveMods = sortedMods
                .Select((mod, index) => (Mod: mod, LoadOrder: index))
                .ToList();

            var newOrderIds = newVirtualActiveMods
                               .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                               .ToList();

            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);

            if (orderChanged)
            {
                _virtualActiveMods = newVirtualActiveMods;
                RaiseListChanged();
                Debug.WriteLine("Active mod list sorted successfully. Order changed.");
                return true;
            }
            else
            {
                // Even if sequence is same, ensure LoadOrder numbers are sequential
                _virtualActiveMods = newVirtualActiveMods;
                RaiseListChanged(); // Raise event to ensure UI reflects sequential numbers if needed
                Debug.WriteLine("Sorting completed. Mod order sequence unchanged.");
                return false;
            }
        }


        // --- Private Helpers ---

        private void ReIndexVirtualActiveMods()
        {
            for (int i = 0; i < _virtualActiveMods.Count; i++)
            {
                _virtualActiveMods[i] = (_virtualActiveMods[i].Mod, i); // Assign sequential load order
            }
        }

        private void RaiseListChanged()
        {
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        // --- Topological Sort Logic (Copied from original ViewModel, now private) ---
        private List<ModItem> TopologicalSortActiveModsWithPriority()
        {
            // *** PASTE the entire TopologicalSortActiveModsWithPriority method here ***
            // (Including the graph building and Kahn's algorithm)
            // Make sure it uses _virtualActiveMods as the source.
            // It should return List<ModItem> or null on failure.

             if (!_virtualActiveMods.Any()) return new List<ModItem>();

            var sortedResult = new List<ModItem>(_virtualActiveMods.Count);
            // Use the mods from the current virtual list
            var activeModsOnly = _virtualActiveMods.Select(vm => vm.Mod).ToList();

            // Build PackageId lookup (lowercase keys for dependency resolution)
            var modLookup = activeModsOnly
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            // Build graph: Adjacency list (mod -> mods that must load AFTER it)
            // and In-degree count (mods that must load BEFORE it)
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
                string modPackageIdLower = mod.PackageId?.ToLowerInvariant();
                if (string.IsNullOrEmpty(modPackageIdLower)) continue; // Skip mods without packageId for rule processing

                // Helper to process dependency lists (LoadAfter, ForceLoadAfter, Dependencies)
                Action<IEnumerable<string>> processLoadAfter = ids =>
                {
                    foreach (var prereqId in ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id.ToLowerInvariant()).Distinct())
                    {
                        if (modLookup.TryGetValue(prereqId, out var prereqMod) && prereqMod != mod)
                        {
                            if (adj.TryGetValue(prereqMod, out var successors) && successors.Add(mod)) // Add edge prereqMod -> mod
                            {
                                inDegree[mod]++;
                            }
                        }
                    }
                };

                // Helper to process LoadBefore/ForceLoadBefore lists
                Action<IEnumerable<string>> processLoadBefore = ids =>
                {
                    foreach (var successorId in ids.Where(id => !string.IsNullOrEmpty(id)).Select(id => id.ToLowerInvariant()).Distinct())
                    {
                        if (modLookup.TryGetValue(successorId, out var successorMod) && successorMod != mod)
                        {
                            if (adj.TryGetValue(mod, out var successors) && successors.Add(successorMod)) // Add edge mod -> successorMod
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

            Comparison<ModItem> priorityComparer = (modA, modB) =>
            {
                if (modA.IsCore != modB.IsCore) return modB.IsCore.CompareTo(modA.IsCore);
                if (!modA.IsCore && modA.IsExpansion != modB.IsExpansion) return modB.IsExpansion.CompareTo(modA.IsExpansion);
                return StringComparer.OrdinalIgnoreCase.Compare(modA.Name, modB.Name);
            };

            while (readyNodes.Count > 0)
            {
                readyNodes.Sort(priorityComparer);
                var current = readyNodes[0];
                readyNodes.RemoveAt(0);
                sortedResult.Add(current);

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
                            Debug.WriteLine($"Critical Error: In-degree for {neighbor.Name} became negative.");
                            // Cycle detection below will handle overall failure
                        }
                     }
                }
            }

            // Check for cycles
            if (sortedResult.Count != activeModsOnly.Count)
            {
                // Log details about the cycle for debugging
                var cycleMods = activeModsOnly.Except(sortedResult).Select(m => m.Name);
                Debug.WriteLine($"Cycle detected during sort. Mods likely involved: {string.Join(", ", cycleMods)}");
                return null; // Indicate failure
            }

            Debug.WriteLine($"Priority topological sort successful. Final Order: {string.Join(", ", sortedResult.Select(m => m.Name))}");
            return sortedResult;
        }

    }
}