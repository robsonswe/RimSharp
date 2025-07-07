using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RimSharp.Shared.Models;

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

        public SortResult TopologicalSort(List<ModItem> mods)
        {
            if (mods == null || mods.Count == 0)
            {
                return new SortResult { IsSuccess = true };
            }

            _logger.LogDebug($"TopologicalSort called with {mods.Count} mods.");

            try
            {
                var modLookup = mods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
                var loadBottomMods = mods.Where(m => m.LoadBottom).ToHashSet();

                var (graph, warnings) = BuildDependencyGraph(mods, modLookup);

                var cycleResult = DetectCyclesWithTarjan(graph);
                if (cycleResult.Any())
                {
                    return new SortResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Circular dependencies were detected, sorting cannot continue.",
                        CyclicDependencies = cycleResult,
                        Warnings = warnings
                    };
                }

                var sortedMods = PerformKahnSort(graph, mods);

                if (sortedMods.Count != mods.Count)
                {
                    // This indicates a graph issue not caught by cycle detection, which can happen
                    // if there are disconnected components that still contain cycles.
                    _logger.LogError("Sort resulted in an incomplete list. This may indicate an issue with the dependency graph.");
                    return new SortResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Topological sort failed. The mod list is inconsistent.",
                        Warnings = warnings
                    };
                }

                var finalMods = ApplyLoadBottomRules(sortedMods, loadBottomMods);

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
        
        private (DependencyGraph graph, List<string> warnings) BuildDependencyGraph(List<ModItem> mods, Dictionary<string, ModItem> modLookup)
        {
            var graph = new DependencyGraph(mods);
            var warnings = new List<string>();

            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) continue;
                
                // Process LoadBefore
                var beforeDeps = mod.LoadBefore.Concat(mod.ForceLoadBefore);
                ProcessDependencies(mod, beforeDeps, modLookup, graph, warnings, isBefore: true);

                // Process LoadAfter
                var afterDeps = mod.LoadAfter.Concat(mod.ForceLoadAfter).Concat(mod.ModDependencies.Select(d => d.PackageId));
                ProcessDependencies(mod, afterDeps, modLookup, graph, warnings, isBefore: false);
            }
            
            // Add implicit dependencies (Core/Expansions first)
            AddImplicitDependencies(mods, graph);

            return (graph, warnings);
        }

        private void ProcessDependencies(ModItem mod, IEnumerable<string> dependencyIds, Dictionary<string, ModItem> lookup, DependencyGraph graph, List<string> warnings, bool isBefore)
        {
            foreach (var depId in dependencyIds.Where(id => !string.IsNullOrEmpty(id)))
            {
                if (lookup.TryGetValue(depId, out var otherMod))
                {
                    if (otherMod == mod) continue; // Mod cannot depend on itself.
                    
                    var from = isBefore ? mod : otherMod;
                    var to = isBefore ? otherMod : mod;
                    graph.AddDependency(from, to, new DependencyInfo(isBefore ? DependencyType.LoadBefore : DependencyType.LoadAfter));
                }
                else
                {
                    warnings.Add($"Mod '{mod.Name}' has a rule referencing '{depId}', which is not in the active mod list.");
                }
            }
        }

        private void AddImplicitDependencies(List<ModItem> mods, DependencyGraph graph)
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
                    if (!graph.HasPath(mod, coreMod) && !graph.HasPath(coreMod, mod))
                    {
                        graph.AddDependency(coreMod, mod, new DependencyInfo(DependencyType.Implicit));
                    }
                }
            }

            // Expansions should load after Core but before other mods.
            foreach (var expansion in expansionMods)
            {
                // Ensure expansion is after Core
                if (coreMod != null && !graph.HasPath(coreMod, expansion))
                {
                    graph.AddDependency(coreMod, expansion, new DependencyInfo(DependencyType.Implicit));
                }

                // Ensure other mods are after expansions
                foreach (var mod in mods)
                {
                    if (mod.ModType != ModType.Core && mod.ModType != ModType.Expansion)
                    {
                        if (!graph.HasPath(mod, expansion) && !graph.HasPath(expansion, mod))
                        {
                            graph.AddDependency(expansion, mod, new DependencyInfo(DependencyType.Implicit));
                        }
                    }
                }
            }
        }

        private List<ModItem> PerformKahnSort(DependencyGraph graph, List<ModItem> mods)
        {
            var sortedList = new List<ModItem>(mods.Count);
            var inDegree = graph.GetInDegrees();
            
            // Using a PriorityQueue to respect sorting priorities.
            var queue = new PriorityQueue<ModItem, int>();

            foreach (var mod in mods)
            {
                if (inDegree[mod] == 0)
                {
                    // Using original priority logic as requested
                    queue.Enqueue(mod, GetPriority(mod));
                }
            }

            while (queue.TryDequeue(out var currentMod, out _))
            {
                sortedList.Add(currentMod);

                foreach (var neighbor in graph.GetDependencies(currentMod).OrderBy(m => GetPriority(m)))
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor, GetPriority(neighbor));
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

            // We can do a simple topological sort on just the bottom mods to respect their internal ordering rules.
            // This is safer than calculating a simple priority number.
            var bottomModLookup = bottomModsToSort.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
            var (bottomGraph, _) = BuildDependencyGraph(bottomModsToSort, bottomModLookup);
            var sortedBottomMods = PerformKahnSort(bottomGraph, bottomModsToSort);

            // If the sub-sort fails, just add them in their current relative order as a fallback.
            if (sortedBottomMods.Count != bottomModsToSort.Count)
            {
                _logger.LogError("Could not determine a stable order for LoadBottom mods due to internal conflicts. Using relative order from main sort.");
                sortedBottomMods = bottomModsToSort;
            }
            
            nonBottomMods.AddRange(sortedBottomMods);
            return nonBottomMods;
        }

        #region Tarjan's Algorithm for Cycle Detection (from v3)

        private List<CycleInfo> DetectCyclesWithTarjan(DependencyGraph graph)
        {
            var cycles = new List<CycleInfo>();
            var index = 0;
            var stack = new Stack<ModItem>();
            var onStack = new HashSet<ModItem>();
            var indices = new Dictionary<ModItem, int>();
            var lowLinks = new Dictionary<ModItem, int>();

            foreach (var mod in graph.AllMods)
            {
                if (!indices.ContainsKey(mod))
                {
                    StrongConnect(mod);
                }
            }

            void StrongConnect(ModItem v)
            {
                indices[v] = index;
                lowLinks[v] = index;
                index++;
                stack.Push(v);
                onStack.Add(v);

                foreach (var w in graph.GetDependencies(v))
                {
                    if (!indices.ContainsKey(w))
                    {
                        StrongConnect(w);
                        lowLinks[v] = Math.Min(lowLinks[v], lowLinks[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
                    }
                }

                if (lowLinks[v] == indices[v])
                {
                    var scc = new List<ModItem>();
                    ModItem w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w);
                        scc.Add(w);
                    } while (v != w);

                    if (scc.Count > 1 || graph.GetDependencies(v).Contains(v))
                    {
                        var description = BuildCycleDescription(scc, graph);
                        cycles.Add(new CycleInfo(scc, description));
                    }
                }
            }

            return cycles;
        }

        private string BuildCycleDescription(List<ModItem> scc, DependencyGraph graph)
        {
            var sb = new StringBuilder();
            sb.AppendLine("A cycle or strongly connected component was found involving:");
            foreach (var mod in scc)
            {
                sb.AppendLine($"  - {mod.Name} ({mod.PackageId})");
            }
            sb.AppendLine("The conflicting rules are:");

            foreach (var fromMod in scc)
            {
                foreach (var toMod in graph.GetDependencies(fromMod))
                {
                    if (scc.Contains(toMod))
                    {
                        var depInfo = graph.GetDependencyInfo(fromMod, toMod);
                        var reason = depInfo.Type switch {
                            DependencyType.LoadBefore => $"'{fromMod.Name}' must load before '{toMod.Name}'",
                            DependencyType.LoadAfter => $"'{toMod.Name}' must load after '{fromMod.Name}'",
                            DependencyType.Implicit => $"Implicit rule: '{fromMod.Name}' should precede '{toMod.Name}'",
                            _ => "An unknown rule"
                        };
                        sb.AppendLine($"  - {reason}");
                    }
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Internal DependencyGraph Class (from v3)

        private class DependencyGraph
        {
            private readonly Dictionary<ModItem, HashSet<ModItem>> _adj = new();
            private readonly Dictionary<ModItem, Dictionary<ModItem, bool>> _pathCache = new();
            private readonly Dictionary<(ModItem from, ModItem to), DependencyInfo> _depInfo = new();

            public DependencyGraph(IEnumerable<ModItem> mods)
            {
                foreach (var mod in mods) _adj[mod] = new HashSet<ModItem>();
            }

            public IEnumerable<ModItem> AllMods => _adj.Keys;
            public void AddDependency(ModItem from, ModItem to, DependencyInfo info)
            {
                if (_adj.ContainsKey(from) && _adj[from].Add(to))
                {
                    _depInfo[(from, to)] = info;
                    _pathCache.Clear(); // Invalidate cache on graph modification
                }
            }
            public HashSet<ModItem> GetDependencies(ModItem mod) => _adj[mod];
            public DependencyInfo GetDependencyInfo(ModItem from, ModItem to) => _depInfo.GetValueOrDefault((from, to));

            public Dictionary<ModItem, int> GetInDegrees()
            {
                var inDegrees = _adj.Keys.ToDictionary(m => m, _ => 0);
                foreach (var neighbors in _adj.Values)
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (inDegrees.ContainsKey(neighbor))
                        {
                            inDegrees[neighbor]++;
                        }
                    }
                }
                return inDegrees;
            }
            
            public bool HasPath(ModItem from, ModItem to)
            {
                if (_pathCache.TryGetValue(from, out var cached) && cached.TryGetValue(to, out var result))
                {
                    return result;
                }
                
                var visited = new HashSet<ModItem>();
                var queue = new Queue<ModItem>();
                queue.Enqueue(from);
                visited.Add(from);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (current == to)
                    {
                        CachePath(from, to, true);
                        return true;
                    }
                    foreach (var neighbor in _adj[current])
                    {
                        if (visited.Add(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                
                CachePath(from, to, false);
                return false;
            }

            private void CachePath(ModItem from, ModItem to, bool result)
            {
                if (!_pathCache.ContainsKey(from))
                {
                    _pathCache[from] = new Dictionary<ModItem, bool>();
                }
                _pathCache[from][to] = result;
            }
        }

        #endregion
    }
}