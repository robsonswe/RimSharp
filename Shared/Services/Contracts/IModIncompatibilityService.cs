using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModIncompatibilityService
    {
        /// <summary>

        /// </summary>
        List<ModIncompatibility> FindIncompatibilities(IEnumerable<ModItem> activeMods);
        
        /// <summary>

        /// </summary>
        List<IncompatibilityGroup> GroupIncompatibilities(List<ModIncompatibility> incompatibilities);
    }
}
