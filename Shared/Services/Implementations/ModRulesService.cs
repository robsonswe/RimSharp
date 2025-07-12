using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics; // For Debug/Console WriteLine

namespace RimSharp.Shared.Services.Implementations
{
    public class ModRulesService : IModRulesService
    {
        private readonly IModRulesRepository _repository;
        private Dictionary<string, ModRule> _cachedRules;
        private bool _rulesLoaded = false;

        public ModRulesService(IModRulesRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public Dictionary<string, ModRule> GetRules()
        {
            // Consider thread safety if needed (using lock)
            if (!_rulesLoaded)
            {
                _cachedRules = _repository.GetAllRules()
                    // Normalize keys to lowercase when loading
                    .ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
                _rulesLoaded = true;
            }
            return _cachedRules;
        }

        public ModRule GetRulesForMod(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return new ModRule(); // Return empty rule if no ID

            var rules = GetRules(); // Ensures rules are loaded

            // Use lowercase for lookup
            if (rules.TryGetValue(packageId.ToLowerInvariant(), out var rule))
            {
                return rule;
            }
            return new ModRule(); // Return empty rule if not found
        }

        // ApplyRulesToMod is less efficient, ApplyRulesToMods is preferred
        public void ApplyRulesToMod(ModItem mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId))
                return;

            ApplyRulesToMods(new List<ModItem> { mod }); // Just call the batch version
        }

        // Modified batch processing method
        public void ApplyRulesToMods(IEnumerable<ModItem> mods)
        {
            var rules = GetRules(); // Load all rules once (uses lowercase keys now)
            if (rules == null || !rules.Any())
            {
                Debug.WriteLine("[DEBUG] ApplyRulesToMods: No rules loaded or available.");
                return; // Nothing to apply
            }
            // Debug.WriteLine($"[DEBUG] ApplyRulesToMods: Processing {rules.Count} rules for mods");

            int versionsAdded = 0;
            int rulesAppliedCount = 0;

            foreach (var mod in mods)
            {
                if (mod == null || string.IsNullOrEmpty(mod.PackageId))
                    continue;

                string packageIdLower = mod.PackageId.ToLowerInvariant(); // Use lowercase for lookup

                if (rules.TryGetValue(packageIdLower, out var rule))
                {
                    rulesAppliedCount++;
                    // Apply supportedVersions from rules
                    if (rule.SupportedVersions != null && rule.SupportedVersions.Count > 0)
                    {
                        // Use a HashSet for efficient duplicate checking (case-insensitive)
                        var existingVersions = mod.SupportedVersions
                                                .Select(v => v.Version)
                                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        foreach (string ruleVersion in rule.SupportedVersions)
                        {
                            if (string.IsNullOrWhiteSpace(ruleVersion)) continue;

                            // Add rule version only if it doesn't already exist
                            if (existingVersions.Add(ruleVersion)) // Returns true if added
                            {
                                mod.SupportedVersions.Add(new VersionSupport(ruleVersion, VersionSource.Database, unofficial: true)); // Mark as DB source and unofficial
                                versionsAdded++;
                                // Debug.WriteLine($"[DEBUG] Added DB support: {mod.PackageId} -> {ruleVersion}");
                            }
                        }
                    }

                    // Apply loadBefore rules
                    if (rule.LoadBefore != null)
                    {
                        foreach (var target in rule.LoadBefore.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(target) && !mod.LoadBefore.Contains(target, StringComparer.OrdinalIgnoreCase))
                            {
                                mod.LoadBefore.Add(target);
                            }
                        }
                    }

                    // Apply loadAfter rules
                    if (rule.LoadAfter != null)
                    {
                        foreach (var target in rule.LoadAfter.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(target) && !mod.LoadAfter.Contains(target, StringComparer.OrdinalIgnoreCase))
                            {
                                mod.LoadAfter.Add(target);
                            }
                        }
                    }

                    // Apply loadBottom
                    if (rule.LoadBottom != null)
                    {
                        // Debug.WriteLine($"[DEBUG] Applying DB LoadBottom={rule.LoadBottom.Value} to mod {mod.PackageId}");
                        mod.LoadBottom = rule.LoadBottom.Value;
                    }

                    // Apply incompatibilities
                    if (rule.Incompatibilities != null)
                    {
                        foreach (var incompatibility in rule.Incompatibilities)
                        {
                            if (!string.IsNullOrWhiteSpace(incompatibility.Key) && !mod.IncompatibleWith.ContainsKey(incompatibility.Key))
                            {
                                mod.IncompatibleWith.Add(incompatibility.Key, incompatibility.Value);

                                // Debug.WriteLine($"[DEBUG] Added DB Incompatibility: {mod.PackageId} -> {target}");
                            }
                        }
                    }
                }
                // else: No rule found for this mod, which is normal.
            }
            // Debug.WriteLine($"[DEBUG] ApplyRulesToMods finished. Applied rules to {rulesAppliedCount} mods. Added {versionsAdded} DB version entries.");
        }

    }
}
