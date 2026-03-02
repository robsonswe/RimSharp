using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Services.Management
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

        public void Remove(ModItem mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId)) return;
            string key = mod.PackageId.ToLowerInvariant();

            if (_modLookup.TryGetValue(key, out var existing) && existing == mod)
            {
                _modLookup.Remove(key);
            }
        }

        public void Register(ModItem mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId)) return;
            string key = mod.PackageId.ToLowerInvariant();
            if (!_modLookup.ContainsKey(key))
            {
                _modLookup[key] = mod;
            }
        }

        public bool TryGetMod(string? packageId, out ModItem? mod)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                mod = null;
                return false;
            }
            return _modLookup.TryGetValue(packageId.ToLowerInvariant(), out mod);
        }

        public Dictionary<string, ModItem> GetLookupDictionary()
        {
            return _modLookup;
        }
    }
}
