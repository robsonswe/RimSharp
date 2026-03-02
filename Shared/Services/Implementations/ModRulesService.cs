using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;

namespace RimSharp.Shared.Services.Implementations
{
    public class ModRulesService : IModRulesService
    {
        private readonly IModRulesRepository _repository;
        private Dictionary<string, ModRule>? _cachedRules;
        private bool _rulesLoaded = false;

        public ModRulesService(IModRulesRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public Dictionary<string, ModRule> GetRules()
        {
            if (!_rulesLoaded || _cachedRules == null)
            {
                _cachedRules = _repository.GetAllRules()
                    .ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
                _rulesLoaded = true;
            }
            return _cachedRules!;
        }

        public ModRule GetRulesForMod(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return new ModRule();

            var rules = GetRules(); 
            if (rules.TryGetValue(packageId.ToLowerInvariant(), out var rule))
            {
                return rule;
            }
            return new ModRule();
        }

        public void ApplyRulesToMod(ModItem mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId))
                return;

            ApplyRulesToMods(new List<ModItem> { mod });
        }

        public void ApplyRulesToMods(IEnumerable<ModItem> mods)
        {
            var rules = GetRules();
            if (rules == null || !rules.Any())
            {
                return;
            }

            foreach (var mod in mods)
            {
                if (mod == null || string.IsNullOrEmpty(mod.PackageId))
                    continue;

                string packageIdLower = mod.PackageId.ToLowerInvariant();

                if (rules.TryGetValue(packageIdLower, out var rule))
                {
                    if (rule.SupportedVersions != null && rule.SupportedVersions.Count > 0)
                    {
                        var existingVersions = mod.SupportedVersions
                                                .Select(v => v.Version)
                                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        foreach (string ruleVersion in rule.SupportedVersions)
                        {
                            if (string.IsNullOrWhiteSpace(ruleVersion)) continue;
                            if (existingVersions.Add(ruleVersion)) 
                            {
                                mod.SupportedVersions.Add(new VersionSupport(ruleVersion, VersionSource.Database, unofficial: true));
                            }
                        }
                    }

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

                    if (rule.LoadBottom != null)
                    {
                        mod.LoadBottom = rule.LoadBottom.Value;
                    }

                    if (rule.Incompatibilities != null && rule.Incompatibilities.Count > 0)
                    {
                        foreach (var incompatibility in rule.Incompatibilities)
                        {
                            if (!string.IsNullOrWhiteSpace(incompatibility.Key) && !mod.IncompatibleWith.ContainsKey(incompatibility.Key) && incompatibility.Value != null)
                            {
                                mod.IncompatibleWith.Add(incompatibility.Key, incompatibility.Value);
                            }
                        }
                    }
                }
            }
        }
    }
}
