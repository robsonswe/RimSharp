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
    }
}
