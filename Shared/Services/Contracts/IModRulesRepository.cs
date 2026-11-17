using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModRulesRepository
    {
        Dictionary<string, ModRule> GetAllRules();
        ModRule GetRulesForMod(string packageId);

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