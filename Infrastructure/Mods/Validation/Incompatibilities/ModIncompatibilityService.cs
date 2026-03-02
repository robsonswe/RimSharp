using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Validation.Incompatibilities
{
    /// <summary>

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

        /// </summary>
        public List<ModIncompatibility> FindIncompatibilities(IEnumerable<ModItem> activeMods)
        {
            var relations = FindIncompatibilityRelations(activeMods);
            return relations.Select(rel => new ModIncompatibility(rel.SourceMod, rel.TargetMod, rel.Reason)).ToList();
        }

        /// <summary>

        /// </summary>
        public List<IncompatibilityGroup> GroupIncompatibilities(List<ModIncompatibility> incompatibilities)
        {
            var relations = incompatibilities.Select(inc => new ModIncompatibilityRelation(inc.SourceMod, inc.TargetMod, inc.Reason)).ToList();
            return GroupIncompatibilityRelations(relations);
        }

        /// <summary>

        /// </summary>
        public List<ModIncompatibilityRelation> FindIncompatibilityRelations(IEnumerable<ModItem> activeMods)
        {
            var relations = new List<ModIncompatibilityRelation>();
            var activeModsLookup = activeMods
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);
            var activeModsWithIds = activeMods.Where(m => !string.IsNullOrEmpty(m.PackageId)).ToList();

            var reverseDependencyGraph = BuildReverseDependencyGraph(activeModsWithIds);

            var processedPairs = new HashSet<(string, string)>();

            foreach (var sourceMod in activeModsWithIds)
            {
                // Combine all incompatibility sources for this mod
                var allIncompatibilities = new Dictionary<string, ModIncompatibilityRule>(sourceMod.IncompatibleWith, StringComparer.OrdinalIgnoreCase);
                foreach (var packageId in sourceMod.IncompatibleWithByVersion)
                {
                    if (!allIncompatibilities.ContainsKey(packageId))
                    {
                        allIncompatibilities[packageId] = new ModIncompatibilityRule(); 
                    }
                }

                foreach (var ruleEntry in allIncompatibilities)
                {
                    var targetPackageId = ruleEntry.Key;
                    var rule = ruleEntry.Value;

                    if (activeModsLookup.TryGetValue(targetPackageId.ToLowerInvariant(), out var targetMod))
                    {
                        var pairKey1 = (sourceMod.PackageId.ToLowerInvariant(), targetMod.PackageId.ToLowerInvariant());
                        var pairKey2 = (targetMod.PackageId.ToLowerInvariant(), sourceMod.PackageId.ToLowerInvariant());

                        if (processedPairs.Contains(pairKey1) || processedPairs.Contains(pairKey2))
                        {
                            continue; // This pair has already been fully processed
                        }

                        GenerateRelationsForPair(sourceMod, targetMod, relations, reverseDependencyGraph, activeModsLookup);

                        // Mark this pair as processed
                        processedPairs.Add(pairKey1);
                    }
                }
            }

            return relations;
        }

        /// <summary>

        /// </summary>
        private void GenerateRelationsForPair(ModItem modA, ModItem modB, List<ModIncompatibilityRelation> relations,
            Dictionary<string, HashSet<string>> reverseDependencyGraph, Dictionary<string, ModItem> activeModsLookup)
        {
            modA.IncompatibleWith.TryGetValue(modB.PackageId, out var ruleFromA);
            modB.IncompatibleWith.TryGetValue(modA.PackageId, out var ruleFromB);

            string reasonForB = GetReason(ruleFromA, modA.Name); 
            string reasonForA = GetReason(ruleFromB, modB.Name); 
            if (ruleFromB == null && ruleFromA != null)
            {
                reasonForA = reasonForB;
            }
            relations.Add(new ModIncompatibilityRelation(modA, modB, reasonForB)); // Reason to remove B
            relations.Add(new ModIncompatibilityRelation(modB, modA, reasonForA)); // Reason to remove A
            var dependentsOfA = FindDependentMods(modA.PackageId, activeModsLookup, reverseDependencyGraph);
            var dependentsOfB = FindDependentMods(modB.PackageId, activeModsLookup, reverseDependencyGraph);

            // Dependents of A are incompatible with B
            foreach (var depA in dependentsOfA)
            {
                relations.Add(new ModIncompatibilityRelation(depA, modB, $"Depends on {modA.Name}, which is incompatible with {modB.Name}"));
                relations.Add(new ModIncompatibilityRelation(modB, depA, $"Incompatible with {modA.Name}, which {depA.Name} depends on"));
            }

            // Dependents of B are incompatible with A
            foreach (var depB in dependentsOfB)
            {
                relations.Add(new ModIncompatibilityRelation(depB, modA, $"Depends on {modB.Name}, which is incompatible with {modA.Name}"));
                relations.Add(new ModIncompatibilityRelation(modA, depB, $"Incompatible with {modB.Name}, which {depB.Name} depends on"));
            }

            // Cross-relate all dependents
            foreach (var depA in dependentsOfA)
            {
                foreach (var depB in dependentsOfB)
                {
                    relations.Add(new ModIncompatibilityRelation(depA, depB, $"Depends on {modA.Name}, which is incompatible with {modB.Name} that {depB.Name} depends on"));
                    relations.Add(new ModIncompatibilityRelation(depB, depA, $"Depends on {modB.Name}, which is incompatible with {modA.Name} that {depA.Name} depends on"));
                }
            }
        }

        private string GetReason(ModIncompatibilityRule? rule, string declarerName)
        {

            if (rule == null) return $"Incompatible according to '{declarerName}'";

            string reason = rule.Comment?.FirstOrDefault() ?? $"Incompatible according to '{declarerName}'";
            if (rule.HardIncompatibility && !reason.Trim().StartsWith("[Hard]", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"[Hard] {reason}";
            }
            return reason;
        }

        /// <summary>

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

            var relatedRelations = allRelations
                .Where(rel => !processedRelations.Contains(rel) &&
                             (group.InvolvedMods.Contains(rel.SourceMod) || 
                              group.InvolvedMods.Contains(rel.TargetMod)))
                .ToList();

            foreach (var related in relatedRelations)
            {
                BuildIncompatibilityGroup(related, group, allRelations, processedRelations);
            }
        }

        private Dictionary<string, HashSet<string>> BuildReverseDependencyGraph(IEnumerable<ModItem> mods)
        {
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in mods)
            {
                if (!graph.ContainsKey(mod.PackageId.ToLowerInvariant()))
                {
                    graph[mod.PackageId.ToLowerInvariant()] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                foreach (var dep in mod.ModDependencies)
                {
                    if (string.IsNullOrEmpty(dep.PackageId)) continue;
                    var depId = dep.PackageId.ToLowerInvariant();
                    if (!graph.ContainsKey(depId))
                    {
                        graph[depId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                    graph[depId].Add(mod.PackageId.ToLowerInvariant());
                }
            }

            return graph;
        }

        private List<ModItem> FindDependentMods(
            string modId, 
            Dictionary<string, ModItem> activeMods, 
            Dictionary<string, HashSet<string>> reverseDependencyGraph)
        {
            var dependentMods = new List<ModItem>();
            if (string.IsNullOrEmpty(modId)) return dependentMods;

            var allDependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (reverseDependencyGraph.TryGetValue(modId.ToLowerInvariant(), out var directDependents))
            {
                foreach(var dep in directDependents) queue.Enqueue(dep);
            }

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                if (visited.Contains(currentId)) continue;

                visited.Add(currentId);
                allDependents.Add(currentId);

                if (reverseDependencyGraph.TryGetValue(currentId, out var nextDependents))
                {
                    foreach (var nextDep in nextDependents)
                    {
                        if (!visited.Contains(nextDep)) queue.Enqueue(nextDep);
                    }
                }
            }

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


