using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModRulesService
    {
        Dictionary<string, ModRule> GetRules();
        ModRule GetRulesForMod(string packageId);
        void ApplyRulesToMod(ModItem mod);
        void ApplyRulesToMods(IEnumerable<ModItem> mods);

        /// <summary>
        /// Drops the cached effective rules (and the repository's merged cache)
        /// so the next application re-reads the source registry and every
        /// source file. Used by the live re-merge after a registry change or a
        /// source refresh.
        /// </summary>
        void RefreshRules();
    }
}