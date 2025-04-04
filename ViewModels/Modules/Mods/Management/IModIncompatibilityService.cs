using RimSharp.Models;
using RimSharp.ViewModels.Modules.Mods.Management;
using System.Collections.Generic;

namespace RimSharp.ViewModels.Modules.Mods.Management
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