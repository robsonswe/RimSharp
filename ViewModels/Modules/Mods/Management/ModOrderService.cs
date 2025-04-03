using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModOrderService
    {
        private readonly List<(ModItem Mod, int LoadOrder)> _virtualActiveMods = new();

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _virtualActiveMods;

        public void Initialize(IEnumerable<(ModItem Mod, int LoadOrder)> initialMods)
        {
            _virtualActiveMods.Clear();
            _virtualActiveMods.AddRange(initialMods);
            ReIndexVirtualActiveMods();
        }

        public void AddMod(ModItem mod, int index)
        {
            index = Math.Clamp(index, 0, _virtualActiveMods.Count);
            _virtualActiveMods.Insert(index, (mod, -1));
            ReIndexVirtualActiveMods();
            Debug.WriteLine($"Added mod '{mod.Name}' at index {index}.");
        }

        public void RemoveMod(ModItem mod)
        {
            int itemIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (itemIndex == -1) return;

            _virtualActiveMods.RemoveAt(itemIndex);
            ReIndexVirtualActiveMods();
            Debug.WriteLine($"Removed mod '{mod.Name}' from order list.");
        }

        public void ReorderMod(ModItem mod, int newIndex)
        {
            int currentIndex = _virtualActiveMods.FindIndex(x => x.Mod == mod);
            if (currentIndex == -1) return;

            newIndex = Math.Clamp(newIndex, 0, _virtualActiveMods.Count - 1);

            if (newIndex == currentIndex || (newIndex == currentIndex + 1 && newIndex > currentIndex))
            {
                return;
            }

            var itemToMove = _virtualActiveMods[currentIndex];
            _virtualActiveMods.RemoveAt(currentIndex);

            int insertIndex = newIndex > currentIndex ? newIndex - 1 : newIndex;
            _virtualActiveMods.Insert(insertIndex, itemToMove);

            ReIndexVirtualActiveMods();
            Debug.WriteLine($"Reordered '{mod.Name}' from {currentIndex} to {insertIndex}");
        }

        public void Clear()
        {
            _virtualActiveMods.Clear();
        }

        public void ReIndexVirtualActiveMods()
        {
            for (int i = 0; i < _virtualActiveMods.Count; i++)
            {
                _virtualActiveMods[i] = (_virtualActiveMods[i].Mod, i);
            }
        }

        public IEnumerable<string> GetActiveModIds()
        {
            return _virtualActiveMods
                .Select(m => m.Mod.PackageId?.ToLowerInvariant())
                .Where(id => !string.IsNullOrEmpty(id));
        }
    }
}