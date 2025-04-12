using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Services.Mangement
{
    public class ModStateTracker
    {
        private readonly HashSet<ModItem> _activeModSet = new();
        private readonly List<ModItem> _allInactiveMods = new();
        private readonly List<string> _missingModIds = new();

        public IReadOnlyList<ModItem> AllInactiveMods => _allInactiveMods;
        public IReadOnlyList<string> MissingModIds => _missingModIds;

        public bool IsModActive(ModItem mod) => mod != null && _activeModSet.Contains(mod);

        public void Initialize(IEnumerable<ModItem> allAvailableMods, Dictionary<string, ModItem> modLookup, IEnumerable<string> activeModPackageIds)
        {
            _allInactiveMods.Clear();
            _activeModSet.Clear();
            _missingModIds.Clear();

            var activeIdList = activeModPackageIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id.ToLowerInvariant())
                .ToList()
                ?? new List<string>();

            foreach (var mod in allAvailableMods)
            {
                if (!IsModInActiveList(mod, activeIdList, modLookup))
                {
                    _allInactiveMods.Add(mod);
                    mod.IsActive = false;
                }
            }

            _allInactiveMods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        public void Activate(ModItem mod)
        {
            if (mod == null || _activeModSet.Contains(mod)) return;
            
            _allInactiveMods.Remove(mod);
            _activeModSet.Add(mod);
            mod.IsActive = true;
            Debug.WriteLine($"Activated mod '{mod.Name}'.");
        }

        public void Deactivate(ModItem mod)
        {
            if (mod == null || !_activeModSet.Contains(mod)) return;
            if (mod.ModType == ModType.Core)
            {
                Debug.WriteLine($"Attempt blocked: Cannot deactivate the Core mod '{mod.Name}'.");
                return;
            }

            _activeModSet.Remove(mod);
            mod.IsActive = false;

            if (!_allInactiveMods.Contains(mod))
            {
                _allInactiveMods.Add(mod);
            }

            Debug.WriteLine($"Deactivated mod '{mod.Name}'.");
        }

        public void ClearActiveMods(IEnumerable<ModItem> modsToKeep)
        {
            var modsToRemove = _activeModSet.Except(modsToKeep).ToList();
            
            foreach (var mod in modsToRemove)
            {
                Deactivate(mod);
            }
        }

        private bool IsModInActiveList(ModItem mod, List<string> activeIdList, Dictionary<string, ModItem> modLookup)
        {
            if (string.IsNullOrEmpty(mod.PackageId)) return false;

            var packageId = mod.PackageId.ToLowerInvariant();
            int index = activeIdList.IndexOf(packageId);

            if (index >= 0)
            {
                if (modLookup.TryGetValue(packageId, out var activeMod) && activeMod == mod)
                {
                    _activeModSet.Add(mod);
                    mod.IsActive = true;
                    return true;
                }
                else
                {
                    _missingModIds.Add(packageId);
                    Debug.WriteLine($"Warning: Active mod ID '{packageId}' from config not found in available mods.");
                }
            }

            return false;
        }
    }
}