using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModDependencySorter
    {
        public List<ModItem> TopologicalSort(
            List<ModItem> mods,
            HashSet<ModItem> hasExplicitLoadBefore,
            HashSet<ModItem> hasExplicitForceLoadBefore)
        {
            if (mods.Count == 0) return new List<ModItem>();

            // Build PackageId lookup for dependency resolution
            var localModLookup = mods
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            // Build dependency graph
            var graph = new Dictionary<ModItem, HashSet<ModItem>>();
            var inDegree = new Dictionary<ModItem, int>();

            foreach (var mod in mods)
            {
                graph[mod] = new HashSet<ModItem>();
                inDegree[mod] = 0;
            }

            // Process dependencies
            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;

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

            return KahnTopologicalSort(mods, graph, inDegree, hasExplicitLoadBefore, hasExplicitForceLoadBefore);
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
            var pq = new List<ModItem>();

            // Add nodes with no dependencies to the initial list
            pq.AddRange(mods.Where(m => inDegree[m] == 0));

            // Initial sort of zero-dependency mods according to priority rules
            pq.Sort((a, b) => CompareMods(a, b, hasExplicitForceLoadBefore, hasExplicitLoadBefore));

            while (pq.Count > 0)
            {
                var current = pq[0];
                pq.RemoveAt(0);
                result.Add(current);

                var neighbors = graph[current].ToList();
                neighbors.Sort((a, b) => CompareMods(a, b, hasExplicitForceLoadBefore, hasExplicitLoadBefore));

                foreach (var neighbor in neighbors)
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
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

            if (result.Count != mods.Count)
            {
                var cycleMods = mods.Except(result).Select(m => m.Name);
                Debug.WriteLine($"Cycle detected: {string.Join(", ", cycleMods)}");
                return null;
            }

            return result;
        }

        private int CompareMods(
            ModItem a,
            ModItem b,
            HashSet<ModItem> forceLoadBeforeMods,
            HashSet<ModItem> loadBeforeMods)
        {
            bool aHasForceLoadBefore = forceLoadBeforeMods.Contains(a);
            bool bHasForceLoadBefore = forceLoadBeforeMods.Contains(b);

            if (aHasForceLoadBefore != bHasForceLoadBefore)
                return aHasForceLoadBefore ? -1 : 1;

            bool aHasLoadBefore = loadBeforeMods.Contains(a);
            bool bHasLoadBefore = loadBeforeMods.Contains(b);

            if (aHasLoadBefore != bHasLoadBefore)
                return aHasLoadBefore ? -1 : 1;

            if (a.IsCore != b.IsCore)
                return a.IsCore ? -1 : 1;

            if (a.IsExpansion != b.IsExpansion)
                return a.IsExpansion ? -1 : 1;

            if (a.IsExpansion && b.IsExpansion)
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}