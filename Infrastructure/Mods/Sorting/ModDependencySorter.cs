using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
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
            "krkr.rocketman",
            "Dubwise.DubsPerformanceAnalyzer.steam",
            "taranchuk.performanceoptimizer"
        };

        public ModDependencySorter(ILogger logger = null)
        {
            _logger = logger ?? new ConsoleLogger();
        }

        public SortResult TopologicalSort(List<ModItem> allMods, CancellationToken ct = default)
        {
            if (allMods == null || allMods.Count == 0)
            {
                return new SortResult { IsSuccess = true };
            }

            _logger.LogDebug($"Starting partitioned sort for {allMods.Count} mods.");

            try
            {
                ct.ThrowIfCancellationRequested();

                var modLookup = allMods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
                var (fullGraph, initialWarnings) = BuildGraph(allMods, modLookup, true, ct);

                _logger.LogDebug("Partitioning mods into tiers.");
                ct.ThrowIfCancellationRequested();
                var tierOneMods = GetTierMods(allMods, fullGraph, TierOnePackageIds, findDependencies: true, ct);

                ct.ThrowIfCancellationRequested();
                var tierThreeStartingSet = allMods
                    .Where(m => m.LoadBottom || KnownTierThreePackageIds.Contains(m.PackageId))
                    .ToHashSet();
                var tierThreeMods = GetTierMods(allMods, fullGraph, tierThreeStartingSet, findDependencies: false, ct);

                ct.ThrowIfCancellationRequested();
                var tierTwoMods = allMods
                    .Except(tierOneMods)
                    .Except(tierThreeMods)
                    .ToList();

                _logger.LogDebug($"Partition complete. Tier 1: {tierOneMods.Count}, Tier 2: {tierTwoMods.Count}, Tier 3: {tierThreeMods.Count}");

                // OPTIMIZATION 1: Pass the master lookup to avoid regenerating it.
                ct.ThrowIfCancellationRequested();
                var sortResultTier1 = SortTier(tierOneMods.ToList(), "Tier 1", modLookup, ct);
                
                ct.ThrowIfCancellationRequested();
                var sortResultTier2 = SortTier(tierTwoMods, "Tier 2", modLookup, ct);
                
                ct.ThrowIfCancellationRequested();
                var sortResultTier3 = SortTier(tierThreeMods.ToList(), "Tier 3", modLookup, ct);

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"An unexpected error occurred during tiered sorting: {ex}");
                return new SortResult { IsSuccess = false, ErrorMessage = "An unexpected error occurred during sorting." };
            }
        }

        private HashSet<ModItem> GetTierMods(List<ModItem> allMods, BidirectionalGraph<ModItem, Edge<ModItem>> fullGraph, HashSet<string> startingPackageIds, bool findDependencies, CancellationToken ct)
        {
            var startingMods = allMods.Where(m => startingPackageIds.Contains(m.PackageId)).ToHashSet();
            return GetTierMods(allMods, fullGraph, startingMods, findDependencies, ct);
        }

        private HashSet<ModItem> GetTierMods(List<ModItem> allMods, BidirectionalGraph<ModItem, Edge<ModItem>> fullGraph, HashSet<ModItem> startingSet, bool findDependencies, CancellationToken ct)
        {
            var tierMods = new HashSet<ModItem>();
            if (startingSet.Count == 0) return tierMods;

            var queue = new Queue<ModItem>(startingSet);

            while (queue.Count > 0)
            {
                // Check cancellation in the loop
                ct.ThrowIfCancellationRequested();

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

        private SortResult SortTier(List<ModItem> tierMods, string tierName, IReadOnlyDictionary<string, ModItem> fullModLookup, CancellationToken ct)
        {
            if (tierMods.Count == 0) return new SortResult { IsSuccess = true };

            _logger.LogDebug($"Sorting {tierName} with {tierMods.Count} mods...");

            // Build a graph for this tier, but use the master lookup to resolve dependencies.
            // This prevents generating redundant "missing from tier" warnings.
            var (graph, _) = BuildGraph(tierMods, fullModLookup, false, ct);

            ct.ThrowIfCancellationRequested();

            var cycles = DetectCycles(graph);
            if (cycles.Any())
            {
                _logger.LogError($"Cycles detected in {tierName}.");
                return new SortResult { IsSuccess = false, CyclicDependencies = cycles };
            }

            var sorted = PerformKahnSort(graph, tierMods, ct);
            if (sorted.Count != tierMods.Count)
            {
                _logger.LogError($"Incomplete sort for {tierName}. Graph may be inconsistent.");
                return new SortResult { IsSuccess = false, ErrorMessage = $"Incomplete sort for {tierName}." };
            }

            _logger.LogDebug($"{tierName} sorting complete.");
            return new SortResult { IsSuccess = true, SortedMods = sorted };
        }

        private void CombineTierResults(SortResult finalResult, SortResult tierResult, List<ModItem> allSortedMods)
        {
            if (!tierResult.IsSuccess)
            {
                finalResult.IsSuccess = false;
            }
            finalResult.CyclicDependencies.AddRange(tierResult.CyclicDependencies);
            finalResult.Warnings.AddRange(tierResult.Warnings); // Warnings from tiers are no longer generated.
            allSortedMods.AddRange(tierResult.SortedMods);
        }
        
        private (BidirectionalGraph<ModItem, Edge<ModItem>> graph, List<string> warnings) BuildGraph(
            List<ModItem> mods, 
            IReadOnlyDictionary<string, ModItem> modLookup, 
            bool generateWarnings,
            CancellationToken ct)
        {
            var graph = new BidirectionalGraph<ModItem, Edge<ModItem>>();
            var warnings = new List<string>();
            var tierModSet = mods.ToHashSet(); // Fast lookups for mods within this build scope.

            graph.AddVertexRange(mods);

            foreach (var mod in mods)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(mod.PackageId)) continue;
                
                // OPTIMIZATION 3: Iterate directly instead of using Concat to reduce allocations.
                void ProcessBeforeDeps(IEnumerable<string> depIds)
                {
                    foreach (var depId in depIds)
                    {
                        if (string.IsNullOrEmpty(depId)) continue;
                        if (modLookup.TryGetValue(depId, out var otherMod) && otherMod != mod)
                        {
                            // Only add edge if the target is also in the current set of mods being processed.
                            if(tierModSet.Contains(otherMod))
                                graph.AddEdge(new Edge<ModItem>(mod, otherMod));
                        }
                        else if (generateWarnings && !modLookup.ContainsKey(depId))
                            warnings.Add($"Mod '{mod.Name}' has a LoadBefore rule for '{depId}', which is not in the active mod list.");
                    }
                }

                void ProcessAfterDeps(IEnumerable<string> depIds)
                {
                    foreach (var depId in depIds)
                    {
                        if (string.IsNullOrEmpty(depId)) continue;
                        if (modLookup.TryGetValue(depId, out var otherMod) && otherMod != mod)
                        {
                            if(tierModSet.Contains(otherMod))
                                graph.AddEdge(new Edge<ModItem>(otherMod, mod));
                        }
                        else if (generateWarnings && !modLookup.ContainsKey(depId))
                            warnings.Add($"Mod '{mod.Name}' has a LoadAfter rule for '{depId}', which is not in the active mod list.");
                    }
                }
                
                ProcessBeforeDeps(mod.LoadBefore);
                ProcessBeforeDeps(mod.ForceLoadBefore);

                ProcessAfterDeps(mod.LoadAfter);
                ProcessAfterDeps(mod.ForceLoadAfter);
                ProcessAfterDeps(mod.ModDependencies.Select(d => d.PackageId));
            }

            // Implicit dependencies are only added on the initial full graph build.
            if (generateWarnings)
            {
                AddImplicitDependencies(mods, graph, ct);
            }

            return (graph, warnings);
        }

        private void AddImplicitDependencies(List<ModItem> mods, BidirectionalGraph<ModItem, Edge<ModItem>> graph, CancellationToken ct)
        {
            var coreMod = mods.FirstOrDefault(m => m.ModType == ModType.Core);
            var expansionMods = mods.Where(m => m.ModType == ModType.Expansion).ToList();

            if (coreMod != null)
            {
                foreach (var mod in mods)
                {
                    ct.ThrowIfCancellationRequested();
                    if (mod == coreMod) continue;
                    // OPTIMIZATION 2: Remove the expensive redundant path check. Keep the essential cycle prevention check.
                    if (!HasPath(graph, mod, coreMod))
                    {
                        graph.AddEdge(new Edge<ModItem>(coreMod, mod));
                    }
                }
            }

            foreach (var expansion in expansionMods)
            {
                if (coreMod != null && coreMod != expansion)
                {
                    // OPTIMIZATION 2: Streamlined check.
                    if (!HasPath(graph, expansion, coreMod))
                    {
                        graph.AddEdge(new Edge<ModItem>(coreMod, expansion));
                    }
                }

                foreach (var mod in mods)
                {
                    ct.ThrowIfCancellationRequested();
                    if (mod.ModType != ModType.Core && mod.ModType != ModType.Expansion)
                    {
                        // OPTIMIZATION 2: Streamlined check.
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

        private List<ModItem> PerformKahnSort(BidirectionalGraph<ModItem, Edge<ModItem>> graph, IEnumerable<ModItem> mods, CancellationToken ct)
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
                // Check cancellation in the loop
                ct.ThrowIfCancellationRequested();

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
            if (mod.LoadAfter.Any() || mod.ForceLoadAfter.Any() || mod.ModDependencies.Any()) return 3;
            if (mod.LoadBefore.Any()) return 5;
            return 4; 
        }
    }
}
