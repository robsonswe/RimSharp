using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModListManager : IModListManager
    {
        private readonly ModStateTracker _stateTracker;
        private readonly ModOrderService _orderService;
        private readonly ModDependencySorter _dependencySorter;
        private readonly ModLookupService _lookupService;
        private readonly List<ModItem> _allAvailableMods = new();

        public event EventHandler ListChanged;

        public ModListManager()
        {
            _stateTracker = new ModStateTracker();
            _orderService = new ModOrderService();
            _dependencySorter = new ModDependencySorter();
            _lookupService = new ModLookupService();
        }

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _orderService.VirtualActiveMods;
        public IReadOnlyList<ModItem> AllInactiveMods => _stateTracker.AllInactiveMods;
        public IReadOnlyList<string> MissingModIds => _stateTracker.MissingModIds;

        public void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds)
        {
            if (allAvailableMods == null) throw new ArgumentNullException(nameof(allAvailableMods));

            _allAvailableMods.Clear();
            _allAvailableMods.AddRange(allAvailableMods);
            _lookupService.Initialize(_allAvailableMods);

            // Build initial active mods list
            var initialActiveMods = new List<(ModItem Mod, int LoadOrder)>();
            var activeIdList = activeModPackageIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id.ToLowerInvariant())
                .ToList()
                ?? new List<string>();

            for (int index = 0; index < activeIdList.Count; index++)
            {
                string packageId = activeIdList[index];
                if (_lookupService.TryGetMod(packageId, out var mod))
                {
                    initialActiveMods.Add((Mod: mod, LoadOrder: index));
                }
            }

            _orderService.Initialize(initialActiveMods);
            _stateTracker.Initialize(_allAvailableMods, _lookupService.GetLookupDictionary(), activeModPackageIds);

            RaiseListChanged();
            Debug.WriteLine($"ModListManager Initialized: {_orderService.VirtualActiveMods.Count} active, {_stateTracker.AllInactiveMods.Count} inactive, {_stateTracker.MissingModIds.Count} missing.");
        }

        public void ActivateMod(ModItem mod) => ActivateModAt(mod, _orderService.VirtualActiveMods.Count);

        public void ActivateModAt(ModItem mod, int index)
        {
            if (mod == null || _stateTracker.IsModActive(mod)) return;

            _stateTracker.Activate(mod);
            _orderService.AddMod(mod, index);
            RaiseListChanged();
        }

        public void DeactivateMod(ModItem mod)
        {
            if (mod == null || !_stateTracker.IsModActive(mod)) return;

            _stateTracker.Deactivate(mod);
            _orderService.RemoveMod(mod);
            RaiseListChanged();
        }

        public void ReorderMod(ModItem mod, int newIndex)
        {
            if (mod == null || !_stateTracker.IsModActive(mod)) return;

            _orderService.ReorderMod(mod, newIndex);
            RaiseListChanged();
        }

        public void ClearActiveList()
        {
            var modsToKeep = _orderService.VirtualActiveMods
                .Where(x => x.Mod.IsCore || x.Mod.IsExpansion)
                .Select(x => x.Mod)
                .ToList();

            _stateTracker.ClearActiveMods(modsToKeep);
            _orderService.Clear();

            // Re-add core/expansion mods
            foreach (var mod in modsToKeep)
            {
                _orderService.AddMod(mod, _orderService.VirtualActiveMods.Count);
            }

            RaiseListChanged();
            Debug.WriteLine($"Cleared active list, kept {modsToKeep.Count} core/expansion mods.");
        }

        public bool SortActiveList()
        {
            if (_orderService.VirtualActiveMods.Count <= 1) return false;

            var originalOrderIds = _orderService.VirtualActiveMods
                .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            // Identify mods with explicit load before
            var activeMods = _orderService.VirtualActiveMods.Select(x => x.Mod).ToList();
            var hasExplicitLoadBefore = new HashSet<ModItem>();
            var hasExplicitForceLoadBefore = new HashSet<ModItem>();

            foreach (var mod in activeMods)
            {
                if (mod.LoadBefore?.Count > 0 || mod.ForceLoadBefore?.Count > 0)
                {
                    hasExplicitLoadBefore.Add(mod);
                    if (mod.ForceLoadBefore?.Count > 0)
                    {
                        hasExplicitForceLoadBefore.Add(mod);
                    }
                }
            }

            // Perform topological sort
            var sortedMods = _dependencySorter.TopologicalSort(activeMods, hasExplicitLoadBefore, hasExplicitForceLoadBefore);
            if (sortedMods == null) return false;

            // Rebuild the virtual active list with new order
            _orderService.Clear();
            for (int i = 0; i < sortedMods.Count; i++)
            {
                _orderService.AddMod(sortedMods[i], i);
            }

            var newOrderIds = _orderService.VirtualActiveMods
                .Select(x => x.Mod.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);
            RaiseListChanged();

            Debug.WriteLine(orderChanged
                ? "Active mod list sorted successfully. Order changed."
                : "Sorting completed. Mod order sequence unchanged.");

            return orderChanged;
        }

        public bool IsModActive(ModItem mod) => _stateTracker.IsModActive(mod);

        public IEnumerable<ModItem> GetAllMods() => _allAvailableMods;

        private void RaiseListChanged() => ListChanged?.Invoke(this, EventArgs.Empty);
    }
}