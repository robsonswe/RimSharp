using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Validation.Incompatibilities
{
    /// <summary>
    /// Group of related incompatibility relations that need to be resolved together
    /// </summary>
    public class IncompatibilityGroup
    {
        public HashSet<ModItem> InvolvedMods { get; } = new HashSet<ModItem>();
        public List<ModIncompatibilityRelation> IncompatibilityRelations { get; } = new List<ModIncompatibilityRelation>();
        
        public void AddIncompatibilityRelation(ModIncompatibilityRelation relation)
        {
            IncompatibilityRelations.Add(relation);
            InvolvedMods.Add(relation.SourceMod);
            InvolvedMods.Add(relation.TargetMod);
        }
    }

    public class ModIncompatibilityService : IModIncompatibilityService
    {
        private readonly ModLookupService _lookupService;
        
        public ModIncompatibilityService(ModLookupService lookupService)
        {
            _lookupService = lookupService ?? throw new ArgumentNullException(nameof(lookupService));
        }

        /// <summary>
        /// Finds all incompatibilities between mods in the active mod list
        /// </summary>
        public List<ModIncompatibility> FindIncompatibilities(IEnumerable<ModItem> activeMods)
        {
            // Call the existing implementation and convert the results
            var relations = FindIncompatibilityRelations(activeMods);
            
            // Convert ModIncompatibilityRelation to ModIncompatibility
            return relations.Select(rel => new ModIncompatibility(
                rel.SourceMod,
                rel.TargetMod,
                rel.Reason
            )).ToList();
        }
        
        /// <summary>
        /// Groups incompatibilities into related sets that need to be resolved together
        /// </summary>
        public List<IncompatibilityGroup> GroupIncompatibilities(List<ModIncompatibility> incompatibilities)
        {
            // Convert ModIncompatibility to ModIncompatibilityRelation
            var relations = incompatibilities.Select(inc => new ModIncompatibilityRelation(
                inc.SourceMod,
                inc.TargetMod,
                inc.Reason
            )).ToList();
            
            // Call the existing implementation with the converted list
            return GroupIncompatibilityRelations(relations);
        }

        /// <summary>
        /// Finds all incompatibility relations between mods in the active mod list
        /// </summary>
        public List<ModIncompatibilityRelation> FindIncompatibilityRelations(IEnumerable<ModItem> activeMods)
        {
            var incompatibilityRelations = new List<ModIncompatibilityRelation>();
            var activeModsLookup = activeMods.ToDictionary(m => m.PackageId?.ToLowerInvariant() ?? string.Empty, m => m, StringComparer.OrdinalIgnoreCase);
            
            // Skip if no package ID or empty
            var activeModsWithIds = activeMods.Where(m => !string.IsNullOrEmpty(m.PackageId)).ToList();
            
            // Create a dependency graph for quick lookup
            var dependencyGraph = BuildDependencyGraph(activeModsWithIds);
            var reverseDependencyGraph = BuildReverseDependencyGraph(activeModsWithIds);
            
            foreach (var mod in activeModsWithIds)
            {
                // Check all incompatible mods (from the new Dictionary property)
                ProcessIncompatibleModsList(mod, mod.IncompatibleWith, activeModsLookup, dependencyGraph, reverseDependencyGraph, incompatibilityRelations);
                
                // Also check version-specific incompatibilities (from the old List<string> property)
                ProcessIncompatibleModsList(mod, mod.IncompatibleWithByVersion, activeModsLookup, dependencyGraph, reverseDependencyGraph, incompatibilityRelations);
            }
            
            return incompatibilityRelations;
        }
        
        /// <summary>
        /// Groups incompatibility relations into related sets that need to be resolved together
        /// </summary>
        public List<IncompatibilityGroup> GroupIncompatibilityRelations(List<ModIncompatibilityRelation> relations)
        {
            var groups = new List<IncompatibilityGroup>();
            var processedRelations = new HashSet<ModIncompatibilityRelation>();
            
            foreach (var relation in relations)
            {
                if (processedRelations.Contains(relation))
                    continue;
                
                var group = new IncompatibilityGroup();
                BuildIncompatibilityGroup(relation, group, relations, processedRelations);
                
                if (group.IncompatibilityRelations.Count > 0)
                {
                    groups.Add(group);
                }
            }
            
            return groups;
        }
        
        private void BuildIncompatibilityGroup(
            ModIncompatibilityRelation seed, 
            IncompatibilityGroup group, 
            List<ModIncompatibilityRelation> allRelations,
            HashSet<ModIncompatibilityRelation> processedRelations)
        {
            if (processedRelations.Contains(seed))
                return;
                
            group.AddIncompatibilityRelation(seed);
            processedRelations.Add(seed);
            
            // Find all relations that share a mod with the current group
            var relatedRelations = allRelations
                .Where(rel => !processedRelations.Contains(rel) &&
                             (group.InvolvedMods.Contains(rel.SourceMod) || 
                              group.InvolvedMods.Contains(rel.TargetMod)))
                .ToList();
            
            // Recursively process related relations
            foreach (var related in relatedRelations)
            {
                BuildIncompatibilityGroup(related, group, allRelations, processedRelations);
            }
        }
        
        private void ProcessIncompatibleModsList( // Overload for IEnumerable<string> (for IncompatibleWithByVersion)
            ModItem sourceMod, 
            IEnumerable<string> incompatibleModIds, 
            Dictionary<string, ModItem> activeModsLookup,
            Dictionary<string, HashSet<string>> dependencyGraph,
            Dictionary<string, HashSet<string>> reverseDependencyGraph,
            List<ModIncompatibilityRelation> results)
        {
            if (incompatibleModIds == null)
                return;
                
            foreach (var incompatibleId in incompatibleModIds)
            {
                if (string.IsNullOrEmpty(incompatibleId))
                    continue;
                    
                var id = incompatibleId.ToLowerInvariant();
                
                // Check if the incompatible mod is in the active list
                if (activeModsLookup.TryGetValue(id, out var incompatibleMod))
                {
                    // Add direct incompatibility relations (bidirectional)
                    results.Add(new ModIncompatibilityRelation(
                        sourceMod, 
                        incompatibleMod, 
                        $"Direct incompatibility with {incompatibleMod.Name}"));
                        
                    results.Add(new ModIncompatibilityRelation(
                        incompatibleMod, 
                        sourceMod, 
                        $"Direct incompatibility with {sourceMod.Name}"));
                    
                    // Find all mods that depend on the incompatible mod (children/dependents)
                    var dependentMods = FindDependentMods(incompatibleMod.PackageId, activeModsLookup, reverseDependencyGraph);
                    
                    // Add dependency incompatibility relations for mods that depend on the incompatible mod
                    foreach (var dependentMod in dependentMods)
                    {
                        // Dependent mod is incompatible with source mod
                        results.Add(new ModIncompatibilityRelation(
                            dependentMod,
                            sourceMod,
                            $"Depends on {incompatibleMod.Name}, which is incompatible with {sourceMod.Name}"));
                            
                        // Source mod is incompatible with dependent mod
                        results.Add(new ModIncompatibilityRelation(
                            sourceMod,
                            dependentMod,
                            $"Incompatible with {incompatibleMod.Name}, which {dependentMod.Name} depends on"));
                    }
                    
                    // Find all mods that depend on the source mod (children/dependents of source)
                    var sourceModDependents = FindDependentMods(sourceMod.PackageId, activeModsLookup, reverseDependencyGraph);
                    
                    // Add source dependent incompatibility relations
                    foreach (var sourceDependent in sourceModDependents)
                    {
                        // Source dependent is incompatible with incompatible mod
                        results.Add(new ModIncompatibilityRelation(
                            sourceDependent,
                            incompatibleMod,
                            $"Depends on {sourceMod.Name}, which is incompatible with {incompatibleMod.Name}"));
                            
                        // Incompatible mod is incompatible with source dependent
                        results.Add(new ModIncompatibilityRelation(
                            incompatibleMod,
                            sourceDependent,
                            $"Incompatible with {sourceMod.Name}, which {sourceDependent.Name} depends on"));
                            
                        // Cross-relate dependents
                        foreach (var dependentMod in dependentMods)
                        {
                            results.Add(new ModIncompatibilityRelation(
                                sourceDependent,
                                dependentMod,
                                $"Depends on {sourceMod.Name}, which is incompatible with {incompatibleMod.Name} that {dependentMod.Name} depends on"));
                                
                            results.Add(new ModIncompatibilityRelation(
                                dependentMod,
                                sourceDependent,
                                $"Depends on {incompatibleMod.Name}, which is incompatible with {sourceMod.Name} that {sourceDependent.Name} depends on"));
                        }
                    }
                }
            }
        }        

        private void ProcessIncompatibleModsList( // Overload for Dictionary<string, ModIncompatibilityRule>
            ModItem sourceMod,
            Dictionary<string, ModIncompatibilityRule> incompatibleMods,
            Dictionary<string, ModItem> activeModsLookup,
            Dictionary<string, HashSet<string>> dependencyGraph,
            Dictionary<string, HashSet<string>> reverseDependencyGraph,
            List<ModIncompatibilityRelation> results)
        {
            if (incompatibleMods == null)
                return;
        
            foreach (var incompatibility in incompatibleMods)
            {
                string incompatibleId = incompatibility.Key;
                var rule = incompatibility.Value;
        
                if (string.IsNullOrEmpty(incompatibleId))
                    continue;
        
                var id = incompatibleId.ToLowerInvariant();
        
                // Check if the incompatible mod is in the active list
                if (activeModsLookup.TryGetValue(id, out var incompatibleMod))
                {
                    // Create a detailed reason from the rule
                    string reason = rule?.Comment?.FirstOrDefault() ?? $"Direct incompatibility with {incompatibleMod.Name}";
                    if (rule?.HardIncompatibility == true && !reason.ToLower().Contains("hard"))
                    {
                        reason = $"[Hard] {reason}";
                    }
        
                    // Add direct incompatibility relation
                    results.Add(new ModIncompatibilityRelation(sourceMod, incompatibleMod, reason));

                    // Note: Bidirectional and transitive relations are not added here to avoid duplicates,
                    // as the loop will eventually process the other mod and its own incompatibility list.
                    // This relies on each mod's defined incompatibilities being processed individually.
                }
            }
        }

        private Dictionary<string, HashSet<string>> BuildDependencyGraph(IEnumerable<ModItem> mods)
        {
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var mod in mods)
            {
                var modId = mod.PackageId.ToLowerInvariant();
                
                // Initialize the set for this mod if it doesn't exist
                if (!graph.ContainsKey(modId))
                {
                    graph[modId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                
                // Process all dependencies
                foreach (var dep in mod.ModDependencies)
                {
                    if (string.IsNullOrEmpty(dep.PackageId))
                        continue;
                        
                    var depId = dep.PackageId.ToLowerInvariant();
                    
                    // Add the dependency to the current mod's dependencies
                    graph[modId].Add(depId);
                }
            }
            
            return graph;
        }
        
        private Dictionary<string, HashSet<string>> BuildReverseDependencyGraph(IEnumerable<ModItem> mods)
        {
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var mod in mods)
            {
                var modId = mod.PackageId.ToLowerInvariant();
                
                // Initialize the set for this mod if it doesn't exist
                if (!graph.ContainsKey(modId))
                {
                    graph[modId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                
                // Process all dependencies
                foreach (var dep in mod.ModDependencies)
                {
                    if (string.IsNullOrEmpty(dep.PackageId))
                        continue;
                        
                    var depId = dep.PackageId.ToLowerInvariant();
                    
                    // Initialize the set for this dependency if it doesn't exist
                    if (!graph.ContainsKey(depId))
                    {
                        graph[depId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                    
                    // Add the current mod as dependent on this dependency
                    graph[depId].Add(modId);
                }
            }
            
            return graph;
        }
        
        private List<ModItem> FindDependentMods(
            string modId, 
            Dictionary<string, ModItem> activeMods, 
            Dictionary<string, HashSet<string>> dependencyGraph)
        {
            var dependentMods = new List<ModItem>();
            var allDependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (string.IsNullOrEmpty(modId) || !dependencyGraph.ContainsKey(modId.ToLowerInvariant()))
                return dependentMods;
                
            // Find all direct dependents
            var modIdLower = modId.ToLowerInvariant();
            
            // BFS to find all dependents
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            
            if (dependencyGraph.ContainsKey(modIdLower))
            {
                foreach (var dependent in dependencyGraph[modIdLower])
                {
                    queue.Enqueue(dependent);
                }
            }
            
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                
                if (visited.Contains(current))
                    continue;
                    
                visited.Add(current);
                allDependents.Add(current);
                
                if (dependencyGraph.ContainsKey(current))
                {
                    foreach (var dependent in dependencyGraph[current])
                    {
                        if (!visited.Contains(dependent))
                        {
                            queue.Enqueue(dependent);
                        }
                    }
                }
            }
            
            // Convert to ModItems
            foreach (var dependentId in allDependents)
            {
                if (activeMods.TryGetValue(dependentId, out var dependentMod))
                {
                    dependentMods.Add(dependentMod);
                }
            }
            
            return dependentMods;
        }
    }
}