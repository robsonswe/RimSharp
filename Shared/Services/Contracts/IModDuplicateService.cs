using RimSharp.Shared.Models;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModDuplicateService
    {
        /// <summary>

        /// </summary>
        List<IGrouping<string, ModItem>> FindDuplicateGroups(IEnumerable<ModItem> allMods);
    }
}

