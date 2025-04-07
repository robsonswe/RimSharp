using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModRulesRepository
    {
        Dictionary<string, ModRule> GetAllRules();
        ModRule GetRulesForMod(string packageId);
    }
}
