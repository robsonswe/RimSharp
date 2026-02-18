using RimSharp.Shared.Models;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModDuplicateService
    {
        /// <summary>
        /// Finds groups of mods that share the same PackageId.
        /// </summary>
        List<IGrouping<string, ModItem>> FindDuplicateGroups(IEnumerable<ModItem> allMods);
    }
}
