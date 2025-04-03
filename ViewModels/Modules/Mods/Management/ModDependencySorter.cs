using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModDependencySorter
    {
        public List<ModItem> TopologicalSort(List<ModItem> mods, HashSet<ModItem> hasExplicitLoadBefore, HashSet<ModItem> hasExplicitForceLoadBefore)
        {
            if (mods.Count == 0) return new List<ModItem>();

            var localModLookup = mods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
            var graph = new Dictionary<ModItem, HashSet<ModItem>>();
            var inDegree = new Dictionary<ModItem, int>();

            foreach (var mod in mods)
            {
                graph[mod] = new HashSet<ModItem>();
                inDegree[mod] = 0;
            }

            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;
                ProcessDependencies(mod, mod.LoadBefore.Concat(mod.ForceLoadBefore), localModLookup, graph, inDegree, isBefore: true);
                ProcessDependencies(mod, mod.LoadAfter.Concat(mod.ForceLoadAfter).Concat(mod.ModDependencies.Select(d => d.PackageId)),
                                    localModLookup, graph, inDegree, isBefore: false);
            }

            var cycle = DetectCycle(graph);
            if (cycle != null)
            {
                Debug.WriteLine($"Cycle detected: {string.Join(" -> ", cycle.Select(m => m.Name))}");
                return null;
            }

            return KahnTopologicalSort(mods, graph, inDegree);
        }

        private void ProcessDependencies(ModItem mod, IEnumerable<string> dependencies,
                                         Dictionary<string, ModItem> lookup,
                                         Dictionary<ModItem, HashSet<ModItem>> graph,
                                         Dictionary<ModItem, int> inDegree, bool isBefore)
        {
            foreach (var depId in dependencies.Where(d => !string.IsNullOrEmpty(d)))
            {
                if (!lookup.TryGetValue(depId, out var otherMod) || otherMod == mod) continue;

                if (isBefore)
                {
                    if (graph[mod].Add(otherMod)) inDegree[otherMod]++;
                }
                else
                {
                    if (graph[otherMod].Add(mod)) inDegree[mod]++;
                }
            }
        }

        private List<ModItem> KahnTopologicalSort(
                                         List<ModItem> mods,
                                         Dictionary<ModItem, HashSet<ModItem>> graph,
                                         Dictionary<ModItem, int> inDegree)
        {
            var result = new List<ModItem>(mods.Count);
            var pq = new PriorityQueue<ModItem, (int, string)>();

            foreach (var mod in mods.Where(m => inDegree[m] == 0))
            {
                pq.Enqueue(mod, (GetPriority(mod), mod.Name));
            }

            while (pq.Count > 0)
            {
                pq.TryDequeue(out var current, out _);
                result.Add(current);

                foreach (var neighbor in graph[current])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        pq.Enqueue(neighbor, (GetPriority(neighbor), neighbor.Name));
                    }
                }
            }

            return result.Count == mods.Count ? result : null;
        }


        private List<ModItem> DetectCycle(Dictionary<ModItem, HashSet<ModItem>> graph)
        {
            var visited = new HashSet<ModItem>();
            var stack = new HashSet<ModItem>();
            var cyclePath = new List<ModItem>();

            bool DFS(ModItem node)
            {
                if (stack.Contains(node))
                {
                    cyclePath.Add(node);
                    return true;
                }
                if (visited.Contains(node)) return false;

                visited.Add(node);
                stack.Add(node);

                foreach (var neighbor in graph[node])
                {
                    if (DFS(neighbor))
                    {
                        cyclePath.Add(node);
                        return true;
                    }
                }

                stack.Remove(node);
                return false;
            }

            foreach (var mod in graph.Keys)
            {
                if (DFS(mod)) return cyclePath.Reverse<ModItem>().ToList();
            }

            return null;
        }

        private int GetPriority(ModItem mod)
        {
            if (mod.ForceLoadBefore.Any()) return 0;
            if (mod.LoadBefore.Any()) return 1;
            if (mod.IsCore) return 2;
            if (mod.IsExpansion) return 3;
            if (mod.LoadAfter.Any() || mod.ForceLoadAfter.Any() || mod.ModDependencies.Any()) return 4;
            return 5;
        }
    }
}
