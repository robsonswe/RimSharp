using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RimSharp.Shared.Models;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.Search;

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

    #region Result and Data Holder Classes

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

    #endregion

    public class ModDependencySorter
    {
        private readonly ILogger _logger;

        private static readonly HashSet<string> TierOnePackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "brrainz.harmony",
            "unlimitedhugs.hugslib",
            "zetrith.prepatcher",
        };

        private static readonly HashSet<string> KnownTierThreePackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "krkr.rocketman"
        };

        public ModDependencySorter(ILogger logger = null)
        {
            _logger = logger ?? new ConsoleLogger();
        }

        public SortResult TopologicalSort(List<ModItem> allMods)
        {
            if (allMods == null || allMods.Count == 0)
            {
                return new SortResult { IsSuccess = true };
            }

            _logger.LogDebug($"Starting partitioned sort for {allMods.Count} mods.");

            try
            {
                var (fullGraph, initialWarnings) = BuildGraph(allMods);

                _logger.LogDebug("Partitioning mods into tiers.");
                var tierOneMods = GetTierMods(allMods, fullGraph, TierOnePackageIds, findDependencies: true);

                var tierThreeStartingSet = allMods
                    .Where(m => m.LoadBottom || KnownTierThreePackageIds.Contains(m.PackageId))
                    .ToHashSet();
                var tierThreeMods = GetTierMods(allMods, fullGraph, tierThreeStartingSet, findDependencies: false);

                var tierTwoMods = allMods
                    .Except(tierOneMods)
                    .Except(tierThreeMods)
                    .ToList();

                _logger.LogDebug($"Partition complete. Tier 1: {tierOneMods.Count}, Tier 2: {tierTwoMods.Count}, Tier 3: {tierThreeMods.Count}");

                var sortResultTier1 = SortTier(tierOneMods.ToList(), "Tier 1");
                var sortResultTier2 = SortTier(tierTwoMods, "Tier 2");
                var sortResultTier3 = SortTier(tierThreeMods.ToList(), "Tier 3");

                var combinedResult = new SortResult { IsSuccess = true };
                var allSortedMods = new List<ModItem>();

                CombineTierResults(combinedResult, sortResultTier1, allSortedMods);
                CombineTierResults(combinedResult, sortResultTier2, allSortedMods);
                CombineTierResults(combinedResult, sortResultTier3, allSortedMods);

                combinedResult.SortedMods = allSortedMods;
                combinedResult.Warnings.AddRange(initialWarnings);

                if (!combinedResult.IsSuccess)
                {
                    combinedResult.ErrorMessage = "Sorting failed in one or more tiers. Check cyclic dependencies for details.";
                }

                return combinedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError($"An unexpected error occurred during tiered sorting: {ex}");
                return new SortResult { IsSuccess = false, ErrorMessage = "An unexpected error occurred during sorting." };
            }
        }

        private HashSet<ModItem> GetTierMods(List<ModItem> allMods, BidirectionalGraph<ModItem, Edge<ModItem>> fullGraph, HashSet<string> startingPackageIds, bool findDependencies)
        {
            var startingMods = allMods.Where(m => startingPackageIds.Contains(m.PackageId)).ToHashSet();
            return GetTierMods(allMods, fullGraph, startingMods, findDependencies);
        }

        private HashSet<ModItem> GetTierMods(List<ModItem> allMods, BidirectionalGraph<ModItem, Edge<ModItem>> fullGraph, HashSet<ModItem> startingSet, bool findDependencies)
        {
            var tierMods = new HashSet<ModItem>();
            if (startingSet.Count == 0) return tierMods;

            var queue = new Queue<ModItem>(startingSet);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (tierMods.Add(current))
                {
                    if (findDependencies)
                    {
                        if (fullGraph.TryGetInEdges(current, out var inEdges))
                        {
                            foreach (var edge in inEdges.Where(e => !tierMods.Contains(e.Source)))
                            {
                                queue.Enqueue(edge.Source);
                            }
                        }
                    }
                    else
                    {
                        if (fullGraph.TryGetOutEdges(current, out var outEdges))
                        {
                            foreach (var edge in outEdges.Where(e => !tierMods.Contains(e.Target)))
                            {
                                queue.Enqueue(edge.Target);
                            }
                        }
                    }
                }
            }
            return tierMods;
        }

        private SortResult SortTier(List<ModItem> tierMods, string tierName)
        {
            if (tierMods.Count == 0) return new SortResult { IsSuccess = true };

            _logger.LogDebug($"Sorting {tierName} with {tierMods.Count} mods...");

            var (graph, warnings) = BuildGraph(tierMods);

            var cycles = DetectCycles(graph);
            if (cycles.Any())
            {
                _logger.LogError($"Cycles detected in {tierName}.");
                return new SortResult { IsSuccess = false, CyclicDependencies = cycles, Warnings = warnings };
            }

            var sorted = PerformKahnSort(graph, tierMods);
            if (sorted.Count != tierMods.Count)
            {
                _logger.LogError($"Incomplete sort for {tierName}. Graph may be inconsistent.");
                return new SortResult { IsSuccess = false, ErrorMessage = $"Incomplete sort for {tierName}." };
            }

            _logger.LogDebug($"{tierName} sorting complete.");
            return new SortResult { IsSuccess = true, SortedMods = sorted, Warnings = warnings };
        }

        private void CombineTierResults(SortResult finalResult, SortResult tierResult, List<ModItem> allSortedMods)
        {
            if (!tierResult.IsSuccess)
            {
                finalResult.IsSuccess = false;
            }
            finalResult.CyclicDependencies.AddRange(tierResult.CyclicDependencies);
            finalResult.Warnings.AddRange(tierResult.Warnings);
            allSortedMods.AddRange(tierResult.SortedMods);
        }

        private (BidirectionalGraph<ModItem, Edge<ModItem>> graph, List<string> warnings) BuildGraph(List<ModItem> mods)
        {
            var graph = new BidirectionalGraph<ModItem, Edge<ModItem>>();
            var warnings = new List<string>();
            var modLookup = mods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            graph.AddVertexRange(mods);

            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;

                var beforeDeps = mod.LoadBefore.Concat(mod.ForceLoadBefore);
                foreach (var depId in beforeDeps.Where(id => !string.IsNullOrEmpty(id)))
                {
                    if (modLookup.TryGetValue(depId, out var otherMod) && otherMod != mod)
                        graph.AddEdge(new Edge<ModItem>(mod, otherMod));
                    else if (!modLookup.ContainsKey(depId))
                        warnings.Add($"Mod '{mod.Name}' has a LoadBefore rule for '{depId}', which is not in the active mod list for this tier.");
                }

                var afterDeps = mod.LoadAfter.Concat(mod.ForceLoadAfter).Concat(mod.ModDependencies.Select(d => d.PackageId));
                foreach (var depId in afterDeps.Where(id => !string.IsNullOrEmpty(id)))
                {
                    if (modLookup.TryGetValue(depId, out var otherMod) && otherMod != mod)
                        graph.AddEdge(new Edge<ModItem>(otherMod, mod));
                    else if (!modLookup.ContainsKey(depId))
                        warnings.Add($"Mod '{mod.Name}' has a LoadAfter rule for '{depId}', which is not in the active mod list for this tier.");
                }
            }

            AddImplicitDependencies(mods, graph);

            return (graph, warnings);
        }

        private void AddImplicitDependencies(List<ModItem> mods, BidirectionalGraph<ModItem, Edge<ModItem>> graph)
        {
            var coreMod = mods.FirstOrDefault(m => m.ModType == ModType.Core);
            var expansionMods = mods.Where(m => m.ModType == ModType.Expansion).ToList();

            if (coreMod != null)
            {
                foreach (var mod in mods)
                {
                    if (mod == coreMod) continue;
                    if (!HasPath(graph, mod, coreMod) && !HasPath(graph, coreMod, mod))
                    {
                        graph.AddEdge(new Edge<ModItem>(coreMod, mod));
                    }
                }
            }

            foreach (var expansion in expansionMods)
            {
                if (coreMod != null && coreMod != expansion && !HasPath(graph, coreMod, expansion))
                {
                    graph.AddEdge(new Edge<ModItem>(coreMod, expansion));
                }

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

        private bool HasPath(IVertexListGraph<ModItem, Edge<ModItem>> graph, ModItem from, ModItem to)
        {
            var bfs = new BreadthFirstSearchAlgorithm<ModItem, Edge<ModItem>>(graph);
            var reached = new HashSet<ModItem>();
            bfs.DiscoverVertex += v => reached.Add(v);
            bfs.Compute(from);
            return reached.Contains(to);
        }

        private List<CycleInfo> DetectCycles(BidirectionalGraph<ModItem, Edge<ModItem>> graph)
        {
            var cycles = new List<CycleInfo>();
            // FIX: Use the overload that takes a pre-initialized dictionary.
            var sccDict = new Dictionary<ModItem, int>();
            graph.StronglyConnectedComponents(sccDict);

            var components = sccDict.GroupBy(kvp => kvp.Value)
                                    .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToList());

            foreach (var component in components.Values.Where(c => c.Count > 1 || (c.Count == 1 && graph.ContainsEdge(c[0], c[0]))))
            {
                var description = BuildCycleDescription(component, graph);
                cycles.Add(new CycleInfo(component, description));
            }
            return cycles;
        }

        private string BuildCycleDescription(List<ModItem> scc, BidirectionalGraph<ModItem, Edge<ModItem>> graph)
        {
            var sb = new StringBuilder();
            sb.AppendLine("A dependency cycle was found involving these mods:");
            foreach (var mod in scc) sb.AppendLine($"  - {mod.Name} ({mod.PackageId})");

            sb.AppendLine("The conflicting rules within this cycle are:");
            foreach (var fromMod in scc)
            {
                if (graph.TryGetOutEdges(fromMod, out var outEdges))
                {
                    foreach (var edge in outEdges.Where(e => scc.Contains(e.Target)))
                    {
                        sb.AppendLine($"  - A rule forces '{fromMod.Name}' to load before '{edge.Target.Name}'.");
                    }
                }
            }
            return sb.ToString();
        }

        private List<ModItem> PerformKahnSort(BidirectionalGraph<ModItem, Edge<ModItem>> graph, IEnumerable<ModItem> mods)
        {
            var sortedList = new List<ModItem>();
            var inDegrees = mods.ToDictionary(mod => mod, mod => graph.InDegree(mod));

            var queue = new PriorityQueue<ModItem, (int, string)>();

            foreach (var mod in mods)
            {
                if (inDegrees[mod] == 0)
                {
                    queue.Enqueue(mod, (GetPriority(mod), mod.PackageId));
                }
            }

            while (queue.TryDequeue(out var currentMod, out _))
            {
                sortedList.Add(currentMod);

                if (graph.TryGetOutEdges(currentMod, out var outEdges))
                {
                    foreach (var edge in outEdges)
                    {
                        var neighbor = edge.Target;
                        inDegrees[neighbor]--;
                        if (inDegrees[neighbor] == 0)
                        {
                            queue.Enqueue(neighbor, (GetPriority(neighbor), neighbor.PackageId));
                        }
                    }
                }
            }

            return sortedList;
        }

        private int GetPriority(ModItem mod)
        {
            if (mod.ForceLoadBefore.Any()) return 0;
            if (mod.ModType == ModType.Core) return 1;
            if (mod.ModType == ModType.Expansion) return 2;
            if (mod.LoadBefore.Any()) return 3;
            if (mod.LoadAfter.Any() || mod.ForceLoadAfter.Any() || mod.ModDependencies.Any()) return 4;
            return 5; // Regular mods
        }
    }
}