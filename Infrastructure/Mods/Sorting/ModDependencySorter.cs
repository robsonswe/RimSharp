using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RimSharp.Shared.Models;
using QuikGraph;
using QuikGraph.Algorithms.Search;
using QuikGraph.Algorithms.Observers;
using QuikGraph.Algorithms;

namespace RimSharp.Infrastructure.Mods.Sorting
{
    // Best-practice: Define interfaces for dependency injection and testing.
    public interface ILogger
    {
        void LogDebug(string message);
        void LogError(string message);
    }

    public class ConsoleLogger : ILogger
    {
        public void LogDebug(string message) => Debug.WriteLine($"[Sorter-DEBUG] {message}");
        public void LogError(string message) => Debug.WriteLine($"[Sorter-ERROR] {message}");
    }

    #region Result and Data Holder Classes (from v3)

    public class SortResult
    {
        public bool IsSuccess { get; set; }
        public List<ModItem> SortedMods { get; set; } = new List<ModItem>();
        public string ErrorMessage { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<CycleInfo> CyclicDependencies { get; set; } = new List<CycleInfo>();
    }

    public class CycleInfo
    {
        public List<ModItem> CyclePath { get; }
        public string Description { get; }

        public CycleInfo(List<ModItem> cyclePath, string description)
        {
            CyclePath = cyclePath;
            Description = description;
        }
    }
    
    internal enum DependencyType { LoadBefore, LoadAfter, ModDependency, Implicit }

    internal class DependencyInfo
    {
        public DependencyType Type { get; }
        public DependencyInfo(DependencyType type) { Type = type; }
    }

    #endregion

    public class ModDependencySorter
    {
        private readonly ILogger _logger;

        public ModDependencySorter(ILogger logger = null)
        {
            _logger = logger ?? new ConsoleLogger();
        }

        /// <summary>
        /// Sorts a list of mods using a topological sort, backed by the QuikGraph library.
        /// It detects cycles and uses a priority queue to resolve order for independent mods.
        /// </summary>
        public SortResult TopologicalSort(List<ModItem> mods)
        {
            if (mods == null || mods.Count == 0)
            {
                return new SortResult { IsSuccess = true };
            }

            _logger.LogDebug($"TopologicalSort called with {mods.Count} mods. Using QuikGraph backend.");

            try
            {
                // Build the graph using QuikGraph's data structures
                var (graph, warnings) = BuildGraph(mods);

                // Detect cycles using QuikGraph's built-in, optimized Tarjan's algorithm
                var cycles = DetectCycles(graph);
                if (cycles.Any())
                {
                    return new SortResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Circular dependencies were detected, sorting cannot continue.",
                        CyclicDependencies = cycles,
                        Warnings = warnings
                    };
                }

                // Perform the sort. We use our custom Kahn's implementation to allow for
                // a PriorityQueue, which QuikGraph's default sorter doesn't support.
                var sortedMods = PerformKahnSort(graph, mods);

                if (sortedMods.Count != mods.Count)
                {
                    _logger.LogError("Sort resulted in an incomplete list. This may indicate a graph issue.");
                    return new SortResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Topological sort failed. The mod list is inconsistent.",
                        Warnings = warnings
                    };
                }

                // Post-processing for LoadBottom mods remains the same.
                var finalMods = ApplyLoadBottomRules(sortedMods, mods.Where(m => m.LoadBottom).ToHashSet());

                return new SortResult
                {
                    IsSuccess = true,
                    SortedMods = finalMods,
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"An unexpected error occurred during sorting: {ex}");
                return new SortResult
                {
                    IsSuccess = false,
                    ErrorMessage = "An unexpected error occurred during sorting. Check logs for details."
                };
            }
        }

        /// <summary>
        /// Builds a QuikGraph AdjacencyGraph from the mod list and their dependency rules.
        /// </summary>
        private (AdjacencyGraph<ModItem, Edge<ModItem>> graph, List<string> warnings) BuildGraph(List<ModItem> mods)
        {
            var graph = new AdjacencyGraph<ModItem, Edge<ModItem>>();
            var warnings = new List<string>();
            var modLookup = mods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            // 1. Add all mods as vertices to the graph
            graph.AddVertexRange(mods);

            // 2. Add edges based on dependency rules
            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;
                
                // LoadBefore: An edge from 'mod' to 'otherMod'
                var beforeDeps = mod.LoadBefore.Concat(mod.ForceLoadBefore);
                foreach (var depId in beforeDeps.Where(id => !string.IsNullOrEmpty(id)))
                {
                    if (modLookup.TryGetValue(depId, out var otherMod) && otherMod != mod)
                        graph.AddEdge(new Edge<ModItem>(mod, otherMod));
                    else if (!modLookup.ContainsKey(depId))
                        warnings.Add($"Mod '{mod.Name}' has a LoadBefore rule for '{depId}', which is not in the active list.");
                }

                // LoadAfter & ModDependencies: An edge from 'otherMod' to 'mod'
                var afterDeps = mod.LoadAfter.Concat(mod.ForceLoadAfter).Concat(mod.ModDependencies.Select(d => d.PackageId));
                foreach (var depId in afterDeps.Where(id => !string.IsNullOrEmpty(id)))
                {
                    if (modLookup.TryGetValue(depId, out var otherMod) && otherMod != mod)
                        graph.AddEdge(new Edge<ModItem>(otherMod, mod));
                    else if (!modLookup.ContainsKey(depId))
                        warnings.Add($"Mod '{mod.Name}' has a LoadAfter rule for '{depId}', which is not in the active list.");
                }
            }

            // 3. Add implicit dependencies (Core/Expansions first)
            AddImplicitDependencies(mods, graph);

            return (graph, warnings);
        }

        private void AddImplicitDependencies(List<ModItem> mods, AdjacencyGraph<ModItem, Edge<ModItem>> graph)
        {
            var coreMod = mods.FirstOrDefault(m => m.ModType == ModType.Core);
            var expansionMods = mods.Where(m => m.ModType == ModType.Expansion).ToList();

            // Core should load before everything else
            if (coreMod != null)
            {
                foreach (var mod in mods)
                {
                    if (mod == coreMod) continue;
                    // Add implicit dependency only if no explicit path already exists between them.
                    if (!HasPath(graph, mod, coreMod) && !HasPath(graph, coreMod, mod))
                    {
                        graph.AddEdge(new Edge<ModItem>(coreMod, mod));
                    }
                }
            }

            // Expansions should load after Core but before other mods.
            foreach (var expansion in expansionMods)
            {
                // Ensure expansion is after Core
                if (coreMod != null && !HasPath(graph, coreMod, expansion))
                {
                    graph.AddEdge(new Edge<ModItem>(coreMod, expansion));
                }

                // Ensure other mods are after expansions
                foreach (var mod in mods)
                {
                    if (mod.ModType != ModType.Core && mod.ModType != ModType.Expansion)
                    {
                        if (!HasPath(graph, mod, expansion) && !HasPath(graph, expansion, mod))
                        {
                            graph.AddEdge(new Edge<ModItem>(expansion, mod));
                        }
                    }
                }
            }
        }

        // Helper to check for path existence using QuikGraph's search algorithms
        private bool HasPath(IVertexListGraph<ModItem, Edge<ModItem>> graph, ModItem from, ModItem to)
        {
            var bfs = new BreadthFirstSearchAlgorithm<ModItem, Edge<ModItem>>(graph);
            var found = false;
            bfs.TreeEdge += (edge) =>
            {
                if (edge.Target.Equals(to))
                    found = true;
            };
            bfs.Compute(from);
            return found;
        }

        /// <summary>
        /// Detects cycles using QuikGraph's optimized implementation of Tarjan's algorithm.
        /// </summary>
        private List<CycleInfo> DetectCycles(AdjacencyGraph<ModItem, Edge<ModItem>> graph)
        {
            var cycles = new List<CycleInfo>();
            var sccDict = new Dictionary<ModItem, int>();
            int componentCount = graph.StronglyConnectedComponents(sccDict);

            var components = new Dictionary<int, List<ModItem>>();
            foreach (var kvp in sccDict)
            {
                if (!components.ContainsKey(kvp.Value))
                    components[kvp.Value] = new List<ModItem>();
                components[kvp.Value].Add(kvp.Key);
            }

            foreach (var component in components.Values)
            {
                if (component.Count > 1)
                {
                    var description = BuildCycleDescription(component, graph);
                    cycles.Add(new CycleInfo(component, description));
                }
                else
                {
                    // Self-loop
                    var mod = component[0];
                    if (graph.TryGetOutEdges(mod, out var outEdges) && outEdges.Any(e => e.Target == mod))
                    {
                        var description = BuildCycleDescription(component, graph);
                        cycles.Add(new CycleInfo(component, description));
                    }
                }
            }

            return cycles;
        }

        private string BuildCycleDescription(List<ModItem> scc, AdjacencyGraph<ModItem, Edge<ModItem>> graph)
        {
            var sb = new StringBuilder();
            sb.AppendLine("A dependency cycle was found involving these mods:");
            foreach (var mod in scc) sb.AppendLine($"  - {mod.Name} ({mod.PackageId})");

            sb.AppendLine("The conflicting rules within this cycle are:");
            foreach (var fromMod in scc)
            {
                if (graph.TryGetOutEdges(fromMod, out var outEdges))
                {
                    foreach (var edge in outEdges)
                    {
                        if (scc.Contains(edge.Target))
                        {
                            sb.AppendLine($"  - A rule forces '{fromMod.Name}' to load before '{edge.Target.Name}'.");
                        }
                    }
                }
            }
            return sb.ToString();
        }

       // In ModDependencySorter.cs

        /// <summary>
        /// Performs Kahn's topological sort using the provided QuikGraph object.
        /// This custom implementation is necessary to support a PriorityQueue for tie-breaking.
        /// </summary>
        private List<ModItem> PerformKahnSort(AdjacencyGraph<ModItem, Edge<ModItem>> graph, IEnumerable<ModItem> mods)
        {
            var sortedList = new List<ModItem>();
            
            // --- FIX START ---
            // The original call to graph.InDegree() inside a LINQ expression was causing a compiler error.
            // This is a more robust and efficient way to calculate all in-degrees.
            var inDegrees = mods.ToDictionary(mod => mod, _ => 0); // 1. Initialize all in-degrees to 0.
            foreach (var edge in graph.Edges)                       // 2. Iterate through all edges once.
            {
                inDegrees[edge.Target]++;                           // 3. Increment the in-degree of the target vertex.
            }
            // --- FIX END ---

            var queue = new PriorityQueue<ModItem, int>();

            foreach (var mod in mods)
            {
                if (inDegrees[mod] == 0)
                {
                    queue.Enqueue(mod, GetPriority(mod));
                }
            }

            while (queue.TryDequeue(out var currentMod, out _))
            {
                sortedList.Add(currentMod);

                // Use QuikGraph to get outgoing edges efficiently
                if (graph.TryGetOutEdges(currentMod, out var outEdges))
                {
                    foreach (var edge in outEdges)
                    {
                        var neighbor = edge.Target;
                        inDegrees[neighbor]--;
                        if (inDegrees[neighbor] == 0)
                        {
                            queue.Enqueue(neighbor, GetPriority(neighbor));
                        }
                    }
                }
            }

            return sortedList;
        }

        /// <summary>
        /// The original priority calculation logic from v1.
        /// </summary>
        private int GetPriority(ModItem mod)
        {
            if (mod.ForceLoadBefore.Any()) return 0;
            if (mod.LoadBefore.Any()) return 1;
            if (mod.ModType == ModType.Core) return 2;
            if (mod.ModType == ModType.Expansion) return 3;
            if (mod.LoadAfter.Any() || mod.ForceLoadAfter.Any() || mod.ModDependencies.Any()) return 4;
            return 5; // Regular mods
        }

        private List<ModItem> ApplyLoadBottomRules(List<ModItem> sortedMods, HashSet<ModItem> loadBottomMods)
        {
            if (!loadBottomMods.Any())
            {
                return sortedMods;
            }

            _logger.LogDebug($"Applying LoadBottom rules to {loadBottomMods.Count} mods.");

            var nonBottomMods = sortedMods.Where(m => !m.LoadBottom).ToList();
            var bottomModsToSort = sortedMods.Where(m => m.LoadBottom).ToList();

            // Use QuikGraph for bottom mods as well
            var bottomModLookup = bottomModsToSort.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
            var (bottomGraph, _) = BuildGraph(bottomModsToSort);
            var sortedBottomMods = PerformKahnSort(bottomGraph, bottomModsToSort);

            if (sortedBottomMods.Count != bottomModsToSort.Count)
            {
                _logger.LogError("Could not determine a stable order for LoadBottom mods due to internal conflicts. Using relative order from main sort.");
                sortedBottomMods = bottomModsToSort;
            }

            nonBottomMods.AddRange(sortedBottomMods);
            return nonBottomMods;
        }
    }
}