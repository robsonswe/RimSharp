using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModLookupService
    {
        private readonly Dictionary<string, ModItem> _modLookup = new(StringComparer.OrdinalIgnoreCase);

        public void Initialize(IEnumerable<ModItem> allAvailableMods)
        {
            _modLookup.Clear();
            foreach (var mod in allAvailableMods.Where(m => !string.IsNullOrEmpty(m.PackageId)))
            {
                var key = mod.PackageId.ToLowerInvariant();
                if (!_modLookup.ContainsKey(key))
                {
                    _modLookup[key] = mod;
                }
            }
        }

        public bool TryGetMod(string packageId, out ModItem mod)
        {
            return _modLookup.TryGetValue(packageId.ToLowerInvariant(), out mod);
        }

        public Dictionary<string, ModItem> GetLookupDictionary()
        {
            return _modLookup;
        }
    }
}