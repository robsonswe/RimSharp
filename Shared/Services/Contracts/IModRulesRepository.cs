using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModRulesRepository
    {
        Dictionary<string, ModRule> GetAllRules();
        ModRule GetRulesForMod(string packageId);

        /// <summary>
        /// Per-packageId provenance of the merged rule set: which source Id
        /// claimed each loadBefore / loadAfter / incompatibleWith target and
        /// the loadTop / loadBottom flags. Keyed by packageId
        /// (OrdinalIgnoreCase) and built / invalidated together with
        /// <see cref="GetAllRules"/>.
        /// </summary>
        IReadOnlyDictionary<string, RuleProvenance> GetAllRuleProvenance();

        /// <summary>
        /// Per-source upstream timestamp (unix seconds) for every source that
        /// provides one, keyed by source Id (OrdinalIgnoreCase). Built /
        /// invalidated together with <see cref="GetAllRules"/>.
        /// </summary>
        IReadOnlyDictionary<string, long> SourceTimestamps { get; }

        /// <summary>
        /// Drops the cached effective rule set so the next read re-resolves the
        /// source registry and re-merges every source file. Called after a
        /// registry change or a source refresh (live re-merge).
        /// </summary>
        void Invalidate();

        /// <summary>
        /// Newest upstream timestamp across the loaded sources ("rules as of"),
        /// or null when no source provides one.
        /// </summary>
        long? EffectiveTimestamp { get; }
    }
}