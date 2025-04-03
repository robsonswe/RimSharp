using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModListManager : IModListManager
    {
        private readonly List<(ModItem Mod, int LoadOrder)> _virtualActiveMods = new();
        private readonly List<ModItem> _allInactiveMods = new();
        private readonly List<ModItem> _allAvailableMods = new();

        // Optimization: Use dictionaries and sets for O(1) lookup operations
        private readonly Dictionary<string, ModItem> _modLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ModItem> _activeModSet = new();

        // Track missing mods
        private readonly List<string> _missingModIds = new();

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _virtualActiveMods;
        public IReadOnlyList<ModItem> AllInactiveMods => _allInactiveMods;
        public IReadOnlyList<string> MissingModIds => _missingModIds;

        public event EventHandler ListChanged;

        public void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds)
        {
            if (allAvailableMods == null) throw new ArgumentNullException(nameof(allAvailableMods));

            _allAvailableMods.Clear();
            _allAvailableMods.AddRange(allAvailableMods);

            var activeIdList = activeModPackageIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id.ToLowerInvariant())
                .ToList()
                ?? new List<string>();

            // Build lookup dictionary for O(1) access
            _modLookup.Clear();
            foreach (var mod in _allAvailableMods.Where(m => !string.IsNullOrEmpty(m.PackageId)))
            {
                if (!_modLookup.ContainsKey(mod.PackageId.ToLowerInvariant()))
                {
                    _modLookup[mod.PackageId.ToLowerInvariant()] = mod;
                }
            }

            // Reset missing mods list
            _missingModIds.Clear();

            // Build active mods from config
            _virtualActiveMods.Clear();
            _activeModSet.Clear();

            for (int index = 0; index < activeIdList.Count; index++)
            {
                string packageId = activeIdList[index];
                if (_modLookup.TryGetValue(packageId, out var mod))
                {
                    _virtualActiveMods.Add((Mod: mod, LoadOrder: index));
                    _activeModSet.Add(mod);
                    mod.IsActive = true;
                }
                else
                {
                    _missingModIds.Add(packageId);
                    Debug.WriteLine($"Warning: Active mod ID '{packageId}' from config not found in available mods.");
                }
            }

            // Determine inactive mods
            _allInactiveMods.Clear();
            foreach (var mod in _allAvailableMods)
            {
                if (!_activeModSet.Contains(mod))
                {
                    _allInactiveMods.Add(mod);
                    mod.IsActive = false;
                }
            }

            // Initial sort of inactive mods
            _allInactiveMods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            RaiseListChanged();
            Debug.WriteLine($"ModListManager Initialized: {_virtualActiveMods.Count} active, {_allInactiveMods.Count} inactive, {_missingModIds.Count} missing.");

            // If there are missing mods, log them for debugging
            if (_missingModIds.Count > 0)
            {
                Debug.WriteLine($"Missing mods: {string.Join(", ", _missingModIds)}");
            }
        }

        public void ActivateMod(ModItem mod) => ActivateModAt(mod, _virtualActiveMods.Count);

        public void ActivateModAt(ModItem mod, int index)
        {
            if (mod == null) return;
            if (_activeModSet.Contains(mod)) return;

            _allInactiveMods.Remove(mod);

            index = Math.Clamp(index, 0, _virtualActiveMods.Count);
            _virtualActiveMods.Insert(index, (mod, -1));
            _activeModSet.Add(mod);
            mod.IsActive = true;

            ReIndexVirtualActiveMods();
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

            int itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex == -1) return;

            _virtualActiveMods.RemoveAt(itemIndex);
            _activeModSet.Remove(mod);
            mod.IsActive = false;

            if (!_allInactiveMods.Contains(mod))
            {
                _allInactiveMods.Add(mod);
                // Note: Not sorting here for performance reasons
            }

            ReIndexVirtualActiveMods();
            RaiseListChanged();
            Debug.WriteLine($"Deactivated mod '{mod.Name}'.");
        }

        public void ReorderMod(ModItem mod, int newIndex)
        {
            if (mod == null) return;

            int currentIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (currentIndex == -1) return;

            newIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count - 1);

            // Skip if moving to same position or adjacent position when moving down
            if (newIndex == currentIndex || (newIndex == currentIndex + 1 && newIndex > currentIndex))
            {
                return;
            }

            var itemToMove = _virtualActiveMods[currentIndex];
            _virtualActiveMods.RemoveAt(currentIndex);

            // Adjust index after removal
            int insertIndex = newIndex > currentIndex ? newIndex - 1 : newIndex;
            _virtualActiveMods.Insert(insertIndex, itemToMove);

            ReIndexVirtualActiveMods();
            RaiseListChanged();
            Debug.WriteLine($"Reordered '{mod.Name}' from {currentIndex} to {insertIndex}");
        }

        public void ClearActiveList()
        {
            var modsToRemove = new List<ModItem>();
            var modsToKeep = new List<(ModItem Mod, int LoadOrder)>();

            foreach (var (mod, loadOrder) in _virtualActiveMods)
            {
                if (!mod.IsCore && !mod.IsExpansion)
                {
                    modsToRemove.Add(mod);
                }
                else
                {
                    modsToKeep.Add((mod, loadOrder));
                }
            }

            if (modsToRemove.Count == 0) return;

            // Sort by original load order
            modsToKeep.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));

            // Update active list
            _virtualActiveMods.Clear();
            _virtualActiveMods.AddRange(modsToKeep);

            // Update tracking collections
            _activeModSet.Clear();
            foreach (var (mod, _) in _virtualActiveMods)
            {
                _activeModSet.Add(mod);
            }

            // Update inactive list
            foreach (var mod in modsToRemove)
            {
                mod.IsActive = false;
                if (!_allInactiveMods.Contains(mod))
                {
                    _allInactiveMods.Add(mod);
                }
            }

            // Sort the inactive list now that many items are added
            _allInactiveMods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            // Clear missing mods list as we're starting fresh
            _missingModIds.Clear();

            ReIndexVirtualActiveMods();
            RaiseListChanged();
            Debug.WriteLine($"Cleared active list, removed {modsToRemove.Count} mods.");
        }

        public bool SortActiveList()
        {
            if (_virtualActiveMods.Count <= 1) return false;

            var originalOrderIds = _virtualActiveMods
                .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            // Perform topological sort
            var sortedMods = TopologicalSortActiveModsWithPriority();
            if (sortedMods == null) return false;

            // Rebuild the virtual active list with new order
            _virtualActiveMods.Clear();
            for (int i = 0; i < sortedMods.Count; i++)
            {
                _virtualActiveMods.Add((sortedMods[i], i));
            }

            var newOrderIds = _virtualActiveMods
                .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);
            RaiseListChanged();

            Debug.WriteLine(orderChanged
                ? "Active mod list sorted successfully. Order changed."
                : "Sorting completed. Mod order sequence unchanged.");

            return true;
        }

        public bool IsModActive(ModItem mod) => mod != null && _activeModSet.Contains(mod);

        public IEnumerable<ModItem> GetAllMods() => _allAvailableMods;

        public IEnumerable<string> GetActiveModIds()
        {
            return _virtualActiveMods
                .Select(m => m.Mod.PackageId?.ToLowerInvariant())
                .Where(id => !string.IsNullOrEmpty(id));
        }

        // Private helper methods
        private void ReIndexVirtualActiveMods()
        {
            for (int i = 0; i < _virtualActiveMods.Count; i++)
            {
                _virtualActiveMods[i] = (_virtualActiveMods[i].Mod, i);
            }
        }

        private void RaiseListChanged() => ListChanged?.Invoke(this, EventArgs.Empty);

        private List<ModItem> TopologicalSortActiveModsWithPriority()
        {
            if (_virtualActiveMods.Count == 0) return new List<ModItem>();

            var sortedResult = new List<ModItem>(_virtualActiveMods.Count);
            var activeModsOnly = _virtualActiveMods.Select(vm => vm.Mod).ToList();

            // Build PackageId lookup for dependency resolution
            var localModLookup = activeModsOnly
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            // Build dependency graph
            var graph = new Dictionary<ModItem, HashSet<ModItem>>();
            var inDegree = new Dictionary<ModItem, int>();

            // Track "LoadBefore" relationships for priority determination
            var hasExplicitLoadBefore = new HashSet<ModItem>();
            var hasExplicitForceLoadBefore = new HashSet<ModItem>();

            foreach (var mod in activeModsOnly)
            {
                graph[mod] = new HashSet<ModItem>();
                inDegree[mod] = 0;
            }

            // Process dependencies
            foreach (var mod in activeModsOnly)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;

                // Track mods with explicit LoadBefore/ForceLoadBefore
                if (mod.LoadBefore?.Count > 0 || mod.ForceLoadBefore?.Count > 0)
                {
                    hasExplicitLoadBefore.Add(mod);
                    if (mod.ForceLoadBefore?.Count > 0)
                    {
                        hasExplicitForceLoadBefore.Add(mod);
                    }
                }

                // Process "load after" relationships
                ProcessDependencies(mod,
                    CombineDependencies(mod.LoadAfter, mod.ForceLoadAfter,
                        mod.ModDependencies?.Select(d => d.PackageId)),
                    localModLookup, graph, inDegree, isLoadAfter: true);

                // Process "load before" relationships
                ProcessDependencies(mod,
                    CombineDependencies(mod.LoadBefore, mod.ForceLoadBefore),
                    localModLookup, graph, inDegree, isLoadAfter: false);
            }

            // Priority-based topological sort
            return KahnTopologicalSort(activeModsOnly, graph, inDegree, hasExplicitLoadBefore, hasExplicitForceLoadBefore);
        }

        private IEnumerable<string> CombineDependencies(params IEnumerable<string>[] dependencies)
        {
            return dependencies
                .Where(dep => dep != null)
                .SelectMany(dep => dep)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id.ToLowerInvariant())
                .Distinct();
        }

        private void ProcessDependencies(
            ModItem mod,
            IEnumerable<string> dependencies,
            Dictionary<string, ModItem> lookup,
            Dictionary<ModItem, HashSet<ModItem>> graph,
            Dictionary<ModItem, int> inDegree,
            bool isLoadAfter)
        {
            foreach (var depId in dependencies)
            {
                if (!lookup.TryGetValue(depId, out var otherMod) || otherMod == mod) continue;

                if (isLoadAfter)
                {
                    // otherMod -> mod (mod loads after otherMod)
                    if (graph[otherMod].Add(mod))
                    {
                        inDegree[mod]++;
                    }
                }
                else
                {
                    // mod -> otherMod (mod loads before otherMod)
                    if (graph[mod].Add(otherMod))
                    {
                        inDegree[otherMod]++;
                    }
                }
            }
        }

        private List<ModItem> KahnTopologicalSort(
            List<ModItem> mods,
            Dictionary<ModItem, HashSet<ModItem>> graph,
            Dictionary<ModItem, int> inDegree,
            HashSet<ModItem> hasExplicitLoadBefore,
            HashSet<ModItem> hasExplicitForceLoadBefore)
        {
            var result = new List<ModItem>(mods.Count);

            // Use a custom priority queue implementation
            var pq = new List<ModItem>();

            // Add nodes with no dependencies to the initial list
            pq.AddRange(mods.Where(m => inDegree[m] == 0));

            // Initial sort of zero-dependency mods according to priority rules
            pq.Sort((a, b) => CompareMods(a, b, hasExplicitForceLoadBefore, hasExplicitLoadBefore));

            while (pq.Count > 0)
            {
                // Take the highest priority mod
                var current = pq[0];
                pq.RemoveAt(0);
                result.Add(current);

                // Process neighbors and update inDegree
                var neighbors = graph[current].ToList();
                neighbors.Sort((a, b) => CompareMods(a, b, hasExplicitForceLoadBefore, hasExplicitLoadBefore));

                foreach (var neighbor in neighbors)
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        // Find the right position to insert based on priority
                        int insertPos = 0;
                        while (insertPos < pq.Count &&
                               CompareMods(pq[insertPos], neighbor, hasExplicitForceLoadBefore, hasExplicitLoadBefore) < 0)
                        {
                            insertPos++;
                        }
                        pq.Insert(insertPos, neighbor);
                    }
                }
            }

            // Check for cycles
            if (result.Count != mods.Count)
            {
                var cycleMods = mods.Except(result).Select(m => m.Name);
                Debug.WriteLine($"Cycle detected: {string.Join(", ", cycleMods)}");
                return null;
            }

            return result;
        }

        // Custom comparison method implementing the priority rules:
        // force/load before > core > expansion > mod dependency > force/load after > mod name
        private int CompareMods(
            ModItem a,
            ModItem b,
            HashSet<ModItem> forceLoadBeforeMods,
            HashSet<ModItem> loadBeforeMods)
        {
            // Force/Load Before has highest priority
            bool aHasForceLoadBefore = forceLoadBeforeMods.Contains(a);
            bool bHasForceLoadBefore = forceLoadBeforeMods.Contains(b);

            if (aHasForceLoadBefore != bHasForceLoadBefore)
                return aHasForceLoadBefore ? -1 : 1;

            bool aHasLoadBefore = loadBeforeMods.Contains(a);
            bool bHasLoadBefore = loadBeforeMods.Contains(b);

            if (aHasLoadBefore != bHasLoadBefore)
                return aHasLoadBefore ? -1 : 1;

            // Core has second highest priority
            if (a.IsCore != b.IsCore)
                return a.IsCore ? -1 : 1;

            // Expansion has third highest priority
            if (a.IsExpansion != b.IsExpansion)
                return a.IsExpansion ? -1 : 1;

            // If both are expansions, sort by name (alphabetically)
            if (a.IsExpansion && b.IsExpansion)
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

            // Otherwise fall back to name
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}