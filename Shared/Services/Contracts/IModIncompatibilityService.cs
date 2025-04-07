using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModIncompatibilityService
    {
        /// <summary>
        /// Finds all incompatibilities between mods in the active mod list
        /// </summary>
        List<ModIncompatibility> FindIncompatibilities(IEnumerable<ModItem> activeMods);
        
        /// <summary>
        /// Groups incompatibilities into related sets that need to be resolved together
        /// </summary>
        List<IncompatibilityGroup> GroupIncompatibilities(List<ModIncompatibility> incompatibilities);
    }
}