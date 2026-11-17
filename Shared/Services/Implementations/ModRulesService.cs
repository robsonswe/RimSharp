using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;

namespace RimSharp.Shared.Services.Implementations
{
    /// <summary>
    /// Applies the effective (merged) rules database onto mods. Application is
    /// provenance-aware: every contribution is recorded on the mod
    /// (<see cref="ModItem.DatabaseLoadBefore"/> — target → claiming source Id —
    /// etc., plus any legacy <see cref="VersionSource.Database"/> version
    /// entries) and stripped before each application, which makes the
    /// operation idempotent and lets a live re-merge remove a
    /// disabled/removed source's contributions without a restart or rescan.
    /// The recorded source Ids (never null/empty) let the UI label every rule
    /// row with its origin via the source's display name. Version suppression
    /// is NOT part of the rules layer anymore (it moved to its own data
    /// source).
    /// </summary>
    public class ModRulesService : IModRulesService
    {
        /// <summary>
        /// Source Id recorded for a database contribution that carries no
        /// merge-provenance claim (repositories without provenance, e.g.
        /// legacy bare <c>JsonModRulesRepository</c> setups or test doubles).
        /// Provenance is always present for the real merged set; this keeps
        /// the recorded value never null/empty so origin labels stay valid.
        /// </summary>
        public const string UnknownSourceId = "unknown";

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

        /// <summary>
        /// Drops the cached effective rules (and the repository's merged cache)
        /// so the next application re-reads the registry and every source file.
        /// </summary>
        public void RefreshRules()
        {
            _repository.Invalidate();
            _rulesLoaded = false;
            _cachedRules = null;
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
            if (mods == null) return;

            var modsList = mods.Where(m => m != null && !string.IsNullOrEmpty(m.PackageId)).ToList();
            if (modsList.Count == 0) return;

            // Strip the previous database contributions first: this is what
            // makes re-application idempotent and makes a removed/disabled
            // source's rules actually disappear from the mods.
            foreach (var mod in modsList)
            {
                StripDatabaseContributions(mod);
            }

            var rules = GetRules();
            if (rules == null || !rules.Any())
            {
                return;
            }

            // Per-packageId merge provenance (which source claimed what).
            // Absent on repositories without provenance; every recorded value
            // then falls back to UnknownSourceId (never null/empty).
            var provenance = _repository.GetAllRuleProvenance();

            foreach (var mod in modsList)
            {
                string packageIdLower = mod.PackageId.ToLowerInvariant();

                if (rules.TryGetValue(packageIdLower, out var rule))
                {
                    // NOTE: rule.SupportedVersions is intentionally NOT applied
                    // here. Version suppression moved out of the rules schema
                    // into its own data source; the rules layer no longer
                    // produces VersionSource.Database entries. The strip below
                    // still removes any legacy Database entries so
                    // re-application stays idempotent.

                    RuleProvenance? claims = null;
                    provenance?.TryGetValue(mod.PackageId, out claims);

                    if (rule.LoadBefore != null)
                    {
                        foreach (var target in rule.LoadBefore.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(target) && !mod.LoadBefore.Contains(target, StringComparer.OrdinalIgnoreCase))
                            {
                                mod.LoadBefore.Add(target);
                                mod.DatabaseLoadBefore[target] = ResolveSourceId(claims?.LoadBeforeSource, target);
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
                                mod.DatabaseLoadAfter[target] = ResolveSourceId(claims?.LoadAfterSource, target);
                            }
                        }
                    }

                    if (rule.LoadTop != null)
                    {
                        mod.LoadTop = rule.LoadTop.Value;
                        mod.DatabaseLoadTop = true;
                        mod.DatabaseLoadTopSource = ResolveSourceId(claims?.LoadTopSource);
                    }

                    if (rule.LoadBottom != null)
                    {
                        mod.LoadBottom = rule.LoadBottom.Value;
                        mod.DatabaseLoadBottom = true;
                        mod.DatabaseLoadBottomSource = ResolveSourceId(claims?.LoadBottomSource);
                    }

                    if (rule.Incompatibilities != null && rule.Incompatibilities.Count > 0)
                    {
                        foreach (var incompatibility in rule.Incompatibilities)
                        {
                            if (!string.IsNullOrWhiteSpace(incompatibility.Key) && !mod.IncompatibleWith.ContainsKey(incompatibility.Key) && incompatibility.Value != null)
                            {
                                mod.IncompatibleWith.Add(incompatibility.Key, incompatibility.Value);
                                mod.DatabaseIncompatibleWith[incompatibility.Key] = ResolveSourceId(claims?.IncompatibleSource, incompatibility.Key);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The source Id claiming one recorded contribution: the merge
        /// provenance's raw Id when present, otherwise
        /// <see cref="UnknownSourceId"/> — the recorded value is never
        /// null/empty (display-name resolution happens later, in the UI).
        /// </summary>
        private static string ResolveSourceId(Dictionary<string, string>? claims, string target)
        {
            if (claims != null && claims.TryGetValue(target, out var sourceId) && !string.IsNullOrWhiteSpace(sourceId))
            {
                return sourceId;
            }

            return UnknownSourceId;
        }

        /// <summary>Flag-level variant of <see cref="ResolveSourceId"/>.</summary>
        private static string ResolveSourceId(string? sourceId) =>
            string.IsNullOrWhiteSpace(sourceId) ? UnknownSourceId : sourceId;

        /// <summary>
        /// Removes exactly the contributions a previous application added:
        /// the tracked load-order targets (iterating the recorded dictionary
        /// keys), the tracked incompatibilities, the database-owned
        /// loadTop/loadBottom flags (and their source ids) and every
        /// <see cref="VersionSource.Database"/> version entry. Contributions
        /// from About.xml (<see cref="ModItem.AboutLoadBefore"/> etc.) or
        /// custom edits are never tracked here and therefore never stripped.
        /// </summary>
        private static void StripDatabaseContributions(ModItem mod)
        {
            foreach (var target in mod.DatabaseLoadBefore.Keys)
            {
                mod.LoadBefore.RemoveAll(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
            }
            mod.DatabaseLoadBefore.Clear();

            foreach (var target in mod.DatabaseLoadAfter.Keys)
            {
                mod.LoadAfter.RemoveAll(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
            }
            mod.DatabaseLoadAfter.Clear();

            foreach (var target in mod.DatabaseIncompatibleWith.Keys)
            {
                mod.IncompatibleWith.Remove(target);
            }
            mod.DatabaseIncompatibleWith.Clear();

            if (mod.DatabaseLoadBottom)
            {
                mod.LoadBottom = false;
                mod.DatabaseLoadBottom = false;
            }
            mod.DatabaseLoadBottomSource = null;

            if (mod.DatabaseLoadTop)
            {
                mod.LoadTop = false;
                mod.DatabaseLoadTop = false;
            }
            mod.DatabaseLoadTopSource = null;

            mod.SupportedVersions.RemoveAll(v => v != null && v.Source == VersionSource.Database);
        }
    }
}
