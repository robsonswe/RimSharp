using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Validation.Duplicates
{
    public class ModDuplicateService : IModDuplicateService
    {
        public List<IGrouping<string, ModItem>> FindDuplicateGroups(IEnumerable<ModItem> allMods)
        {
            if (allMods == null) return new List<IGrouping<string, ModItem>>();

            return allMods
                .Where(m => !string.IsNullOrEmpty(m.PackageId))
                .GroupBy(m => m.PackageId.ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .ToList();
        }
    }
}
