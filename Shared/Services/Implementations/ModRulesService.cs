using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

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
            if (!_rulesLoaded)
            {
                _cachedRules = _repository.GetAllRules();
                _rulesLoaded = true;
            }
            return _cachedRules;
        }

        public ModRule GetRulesForMod(string packageId)
        {
            if (!_rulesLoaded)
            {
                _cachedRules = GetRules();
            }

            if (_cachedRules.TryGetValue(packageId, out var rule))
            {
                return rule;
            }
            return new ModRule();
        }

        public void ApplyRulesToMod(ModItem mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId))
                return;

            var rule = GetRulesForMod(mod.PackageId);

            if (rule == null) return;

            // Apply loadBefore rules
            foreach (var target in rule.LoadBefore.Keys)
            {
                if (!mod.LoadBefore.Contains(target))
                    mod.LoadBefore.Add(target);
            }

            // Apply loadAfter rules
            foreach (var target in rule.LoadAfter.Keys)
            {
                if (!mod.LoadAfter.Contains(target))
                    mod.LoadAfter.Add(target);
            }

            // Apply loadBottom
            if (rule.LoadBottom != null)
            {
                mod.LoadBottom = rule.LoadBottom.Value;
                    Console.WriteLine($"[DEBUG] Applied LoadBottom={rule.LoadBottom.Value} to mod {mod.PackageId}");
            }

            // Apply incompatibilities
            foreach (var target in rule.Incompatibilities.Keys)
            {
                if (!mod.IncompatibleWith.Contains(target))
                    mod.IncompatibleWith.Add(target);
            }
        }

        // New batch processing method
    public void ApplyRulesToMods(IEnumerable<ModItem> mods)
{
    // Load all rules once
    var rules = GetRules();
    Console.WriteLine($"[DEBUG] ApplyRulesToMods: Processing {rules.Count} rules for mods");

    foreach (var mod in mods)
    {
        if (mod == null || string.IsNullOrEmpty(mod.PackageId))
            continue;

        string packageIdLower = mod.PackageId.ToLowerInvariant(); // Ensure case-insensitive lookup
        
        if (rules.TryGetValue(packageIdLower, out var rule))
        {
            Console.WriteLine($"[DEBUG] Found rule for mod: {mod.PackageId} / {packageIdLower}");
            
            // Apply loadBefore rules
            foreach (var target in rule.LoadBefore.Keys)
            {
                if (!mod.LoadBefore.Contains(target))
                {
                    mod.LoadBefore.Add(target);
                    Console.WriteLine($"[DEBUG] Added LoadBefore: {mod.PackageId} -> {target}");
                }
            }

            // Apply loadAfter rules
            foreach (var target in rule.LoadAfter.Keys)
            {
                if (!mod.LoadAfter.Contains(target))
                {
                    mod.LoadAfter.Add(target);
                    Console.WriteLine($"[DEBUG] Added LoadAfter: {mod.PackageId} -> {target}");
                }
            }

            // Apply loadBottom - with detailed logging
            if (rule.LoadBottom != null)
            {
                Console.WriteLine($"[DEBUG] LoadBottom rule found for {mod.PackageId}: Value={rule.LoadBottom.Value}, Comment={string.Join(", ", rule.LoadBottom.Comment)}");
                mod.LoadBottom = rule.LoadBottom.Value;
                Console.WriteLine($"[DEBUG] Applied LoadBottom={rule.LoadBottom.Value} to mod {mod.PackageId}");
            }

            // Apply incompatibilities
            foreach (var target in rule.Incompatibilities.Keys)
            {
                if (!mod.IncompatibleWith.Contains(target))
                {
                    mod.IncompatibleWith.Add(target);
                    Console.WriteLine($"[DEBUG] Added Incompatibility: {mod.PackageId} -> {target}");
                }
            }
        }
        else
        {
            // Check if it's a case sensitivity issue
            var matchingKey = rules.Keys.FirstOrDefault(k => string.Equals(k, mod.PackageId, StringComparison.OrdinalIgnoreCase));
            if (matchingKey != null)
            {
                Console.WriteLine($"[DEBUG] Case-sensitive mismatch for {mod.PackageId}. Found rule with key: {matchingKey}");
            }
        }
    }
}
    
    }

}
