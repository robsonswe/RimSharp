using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Shared.Models;

namespace RimSharp.Infrastructure.Mods.Sorting
{
    public class ModDependencySorter
    {
        public List<ModItem> TopologicalSort(List<ModItem> mods, HashSet<ModItem> hasExplicitLoadBefore, HashSet<ModItem> hasExplicitForceLoadBefore)
        {
            if (mods.Count == 0) return new List<ModItem>();

            Console.WriteLine($"[DEBUG] TopologicalSort called with {mods.Count} mods");

            var localModLookup = mods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
            var graph = new Dictionary<ModItem, HashSet<ModItem>>();
            var inDegree = new Dictionary<ModItem, int>();

            var coreMod = mods.FirstOrDefault(m => m.IsCore);
            var expansionMods = mods.Where(m => m.IsExpansion).ToList();
            var loadBottomMods = mods.Where(m => m.LoadBottom).ToList();

            Console.WriteLine($"[DEBUG] Found {loadBottomMods.Count} LoadBottom mods: {string.Join(", ", loadBottomMods.Select(m => m.PackageId))}");

            foreach (var mod in mods)
            {
                graph[mod] = new HashSet<ModItem>();
                inDegree[mod] = 0;
            }

            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;

                ProcessDependencies(mod, mod.LoadBefore.Concat(mod.ForceLoadBefore), localModLookup, graph, inDegree, isBefore: true);
                ProcessDependencies(mod, mod.LoadAfter.Concat(mod.ForceLoadAfter).Concat(mod.ModDependencies.Select(d => d.PackageId)), localModLookup, graph, inDegree, isBefore: false);
            }

            if (coreMod != null)
            {
                AddImplicitDependencies(mods, coreMod, graph, inDegree);
            }

            foreach (var expansionMod in expansionMods)
            {
                AddImplicitDependencies(mods, expansionMod, graph, inDegree);
            }

            // IMPORTANT: We no longer add LoadBottom dependencies here to avoid cycles
            // This will be handled after the topological sort

            if (coreMod != null)
            {
                foreach (var expansionMod in expansionMods)
                {
                    if (!HasExplicitOrImpliedBeforeDependency(expansionMod, coreMod))
                    {
                        if (graph[coreMod].Add(expansionMod))
                            inDegree[expansionMod]++;
                    }
                }
            }

            var cycle = DetectCycle(graph);
            if (cycle != null)
            {
                Console.WriteLine($"[DEBUG] Cycle detected: {string.Join(" -> ", cycle.Select(m => m.Name))}");
                return null;
            }

            // Get the standard topological sort
            var sortedMods = KahnTopologicalSort(mods, graph, inDegree);

            if (sortedMods == null || sortedMods.Count != mods.Count)
            {
                Console.WriteLine($"[DEBUG] Topological sort failed or returned incomplete list");
                return null;
            }

            // NEW APPROACH: Now, move all LoadBottom mods to the end
            // We do this as a post-processing step, maintaining their relative order
            if (loadBottomMods.Count > 0)
            {
                Console.WriteLine($"[DEBUG] Moving LoadBottom mods to the end of the sorted list");

                // Remove all LoadBottom mods from the sorted list
                var nonBottomMods = sortedMods.Where(m => !m.LoadBottom).ToList();

                // Add all LoadBottom mods back at the end
                // Sort them by priority first
                var sortedBottomMods = loadBottomMods.OrderBy(m =>
                {
                    // Start with higher priority values for more important ones
                    int priority = 0;

                    // If a LoadBottom mod is marked as loadAfter another LoadBottom mod,
                    // it should come after it in the final order
                    foreach (var otherBottomMod in loadBottomMods)
                    {
                        if (otherBottomMod == m) continue;

                        if (m.LoadAfter.Contains(otherBottomMod.PackageId, StringComparer.OrdinalIgnoreCase) ||
                            m.ForceLoadAfter.Contains(otherBottomMod.PackageId, StringComparer.OrdinalIgnoreCase))
                        {
                            priority++;
                        }

                        if (otherBottomMod.LoadBefore.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase) ||
                            otherBottomMod.ForceLoadBefore.Contains(m.PackageId, StringComparer.OrdinalIgnoreCase))
                        {
                            priority++;
                        }
                    }

                    return priority;
                }).ToList();

                // Combine the lists
                var result = new List<ModItem>(nonBottomMods.Count + sortedBottomMods.Count);
                result.AddRange(nonBottomMods);
                result.AddRange(sortedBottomMods);

                // Log the positions of LoadBottom mods
                foreach (var mod in sortedBottomMods)
                {
                    int position = result.IndexOf(mod);
                    Console.WriteLine($"[DEBUG] LoadBottom mod {mod.PackageId} placed at position {position} of {result.Count}");
                }

                return result;
            }

            return sortedMods;
        }


        private void AddImplicitDependencies(List<ModItem> mods, ModItem specialMod,
                                     Dictionary<ModItem, HashSet<ModItem>> graph,
                                     Dictionary<ModItem, int> inDegree)
        {
            foreach (var mod in mods)
            {
                // Avoid forcing Core to come after anything
                if (mod == specialMod || mod.IsCore) continue;

                if (HasExplicitOrImpliedBeforeDependency(mod, specialMod)) continue;

                if (graph[specialMod].Add(mod))
                    inDegree[mod]++;
            }
        }

        private bool HasExplicitBeforeDependency(ModItem mod, ModItem target)
        {
            return mod.LoadBefore.Contains(target.PackageId, StringComparer.OrdinalIgnoreCase) ||
                   mod.ForceLoadBefore.Contains(target.PackageId, StringComparer.OrdinalIgnoreCase);
        }

        private bool HasExplicitAfterDependency(ModItem mod, ModItem target)
        {
            return mod.LoadAfter.Contains(target.PackageId, StringComparer.OrdinalIgnoreCase) ||
                   mod.ForceLoadAfter.Contains(target.PackageId, StringComparer.OrdinalIgnoreCase) ||
                   mod.ModDependencies.Any(d => string.Equals(d.PackageId, target.PackageId, StringComparison.OrdinalIgnoreCase));
        }

        private bool HasExplicitOrImpliedBeforeDependency(ModItem mod, ModItem target)
        {
            if (HasExplicitBeforeDependency(mod, target)) return true;

            if (target.IsExpansion &&
                mod.LoadBefore.Contains("ludeon.rimworld", StringComparer.OrdinalIgnoreCase))
                return true;

            return false;
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

            Console.WriteLine("[DEBUG] Starting KahnTopologicalSort");

            foreach (var mod in mods.Where(m => inDegree[m] == 0))
            {
                int priority = GetPriority(mod);
                pq.Enqueue(mod, (priority, mod.Name));
            }

            while (pq.Count > 0)
            {
                pq.TryDequeue(out var current, out var priority);
                result.Add(current);

                foreach (var neighbor in graph[current])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        int neighborPriority = GetPriority(neighbor);
                        pq.Enqueue(neighbor, (neighborPriority, neighbor.Name));
                    }
                }
            }

            Console.WriteLine($"[DEBUG] KahnTopologicalSort completed. Result count: {result.Count}, Expected: {mods.Count}");

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
            return 5; // Regular mods
        }


    }
}