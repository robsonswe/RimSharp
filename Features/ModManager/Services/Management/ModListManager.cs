#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Infrastructure.Mods.Sorting;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.ModManager.Services.Management
{
    public class ModListManager : IModListManager
    {
        private readonly ModStateTracker _stateTracker;
        private readonly ModOrderService _orderService;
        private readonly ModDependencySorter _dependencySorter;
        private readonly ModLookupService _lookupService;
        private readonly IModDictionaryService _modDictionaryService;
        private readonly List<ModItem> _allAvailableMods = new();
        private bool _hasAnyActiveModIssues = false;

        public event EventHandler? ListChanged;

        public ModListManager(IModDictionaryService modDictionaryService)
        {
            _modDictionaryService = modDictionaryService ?? throw new ArgumentNullException(nameof(modDictionaryService));

            _stateTracker = new ModStateTracker();
            _orderService = new ModOrderService();
            _dependencySorter = new ModDependencySorter();
            _lookupService = new ModLookupService();
        }

        public IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods => _orderService.VirtualActiveMods;
        public IReadOnlyList<ModItem> AllInactiveMods => _stateTracker.AllInactiveMods;
        public bool HasAnyActiveModIssues => _hasAnyActiveModIssues;

        public void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds)
        {
            if (allAvailableMods == null) throw new ArgumentNullException(nameof(allAvailableMods));

            _allAvailableMods.Clear();
            _allAvailableMods.AddRange(allAvailableMods);
            _lookupService.Initialize(_allAvailableMods);

            var activeIdList = activeModPackageIds?
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id.ToLowerInvariant())
                .ToList()
                ?? new List<string>();

            _stateTracker.Initialize(_allAvailableMods, _lookupService.GetLookupDictionary(), activeIdList);

            var initialActiveModsForOrder = new List<(ModItem Mod, int LoadOrder)>();
            for (int index = 0; index < activeIdList.Count; index++)
            {
                string packageId = activeIdList[index];
                if (_lookupService.TryGetMod(packageId, out var mod) && _stateTracker.IsModActive(mod))
                {
                    initialActiveModsForOrder.Add((Mod: mod, LoadOrder: index));
                }
            }

            _orderService.Initialize(initialActiveModsForOrder);
            RecalculateActiveModIssues();

            RaiseListChanged();
            Debug.WriteLine($"ModListManager Initialized: {_orderService.VirtualActiveMods.Count} active, {_stateTracker.AllInactiveMods.Count} inactive.");
        }

        public void ActivateMod(ModItem mod) => ActivateModsAt(new[] { mod }, _orderService.VirtualActiveMods.Count);
        public void ActivateModAt(ModItem mod, int index) => ActivateModsAt(new[] { mod }, index);
        public void DeactivateMod(ModItem mod) => DeactivateMods(new[] { mod });
        public void ReorderMod(ModItem mod, int newIndex) => ReorderMods(new[] { mod }, newIndex);

        public void ClearActiveList()
        {
            var modsToKeep = _orderService.VirtualActiveMods
                .Where(x => x.Mod != null && (x.Mod.ModType == ModType.Core || x.Mod.ModType == ModType.Expansion))
                .Select(x => x.Mod)
                .ToList();

            var modsToDeactivate = _orderService.VirtualActiveMods
                                     .Select(x => x.Mod)
                                     .Except(modsToKeep)
                                     .ToList();

            DeactivateMods(modsToDeactivate);
            Debug.WriteLine($"Cleared active list, kept {modsToKeep.Count} core/expansion mods.");
        }

        public bool SortActiveList(System.Threading.CancellationToken ct = default)
        {
            var currentActiveMods = _orderService.VirtualActiveMods.Select(x => x.Mod).ToList();
            if (currentActiveMods.Count <= 1) return false;

            var originalOrderIds = currentActiveMods.Select(mod => mod.PackageId.ToLowerInvariant()).ToList();
            var sortResult = _dependencySorter.TopologicalSort(currentActiveMods, ct);

            if (!sortResult.IsSuccess) return false;

            var sortedMods = sortResult.SortedMods;
            if (sortedMods.Count != currentActiveMods.Count) return false;

            var newOrderIds = sortedMods.Select(mod => mod.PackageId.ToLowerInvariant()).ToList();
            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);

            if (orderChanged)
            {
                _orderService.Clear();
                _orderService.AddModsAt(sortedMods, 0);
                RaiseListChanged();
            }

            return orderChanged;
        }

        public bool IsModActive(ModItem mod) => _stateTracker.IsModActive(mod);
        public IEnumerable<ModItem> GetAllMods() => _allAvailableMods;

        private void RaiseListChanged()
        {
            RecalculateActiveModIssues();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ActivateModsAt(IEnumerable<ModItem> mods, int index)
        {
            if (mods == null) return;
            bool listChanged = false;

            var modsToProcess = mods.Where(m => m != null).Distinct().ToList();
            if (!modsToProcess.Any()) return;

            var modsToActivate = modsToProcess.Where(m => !_stateTracker.IsModActive(m)).ToList();
            var modsToReorder = modsToProcess.Where(m => _stateTracker.IsModActive(m)).ToList();

            if (modsToReorder.Any())
            {
                listChanged |= _orderService.ReorderMods(modsToReorder, index);
                var lastReorderedMod = modsToReorder.LastOrDefault(m => _orderService.VirtualActiveMods.Any(vm => vm.Mod == m));
                if (lastReorderedMod != null)
                {
                    index = _orderService.VirtualActiveMods.ToList().FindIndex(vm => vm.Mod == lastReorderedMod) + 1;
                }
                index = Math.Clamp(index, 0, _orderService.VirtualActiveMods.Count);
            }

            if (modsToActivate.Any())
            {
                foreach (var mod in modsToActivate) _stateTracker.Activate(mod);
                listChanged |= _orderService.AddModsAt(modsToActivate, index);
            }

            if (listChanged) RaiseListChanged();
        }

        public void DeactivateMods(IEnumerable<ModItem> mods)
        {
            if (mods == null) return;
            var modsToDeactivate = mods.Where(m => m != null && m.ModType != ModType.Core).Distinct().ToList();
            if (!modsToDeactivate.Any()) return;

            foreach (var mod in modsToDeactivate) _stateTracker.Deactivate(mod);
            if (_orderService.RemoveMods(modsToDeactivate)) RaiseListChanged();
        }

        public void ReorderMods(IEnumerable<ModItem> modsToMove, int targetIndex)
        {
            if (modsToMove == null) return;
            var validModsToMove = modsToMove.Where(m => m != null && _stateTracker.IsModActive(m)).Distinct().ToList();
            if (!validModsToMove.Any()) return;

            if (_orderService.ReorderMods(validModsToMove, targetIndex)) RaiseListChanged();
        }

        public (List<ModItem> addedMods, List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)> missingDependencies) ResolveDependencies()
        {
            var addedMods = new List<ModItem>();
            bool listChanged = false;

            var activeModsForCheck = _orderService.VirtualActiveMods.Select(x => x.Mod).ToList();
            
            // Build inactive lookup robustly
            var inactiveModsLookup = new Dictionary<string, ModItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in _stateTracker.AllInactiveMods)
            {
                if (!string.IsNullOrEmpty(mod.PackageId)) inactiveModsLookup.TryAdd(mod.PackageId, mod);
            }

            var activePackageIds = new HashSet<string>(_orderService.GetActiveModIds(), StringComparer.OrdinalIgnoreCase);
            var modsToCheckQueue = new Queue<ModItem>(activeModsForCheck);
            var processedDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingDepsDict = new Dictionary<string, (string displayName, string steamUrl, List<string> requiredBy)>(StringComparer.OrdinalIgnoreCase);

            while (modsToCheckQueue.Count > 0)
            {
                var currentMod = modsToCheckQueue.Dequeue();
                if (currentMod?.ModDependencies == null) continue;

                foreach (var dependency in currentMod.ModDependencies)
                {
                    if (string.IsNullOrWhiteSpace(dependency.PackageId)) continue;
                    var depPackageIdLower = dependency.PackageId.ToLowerInvariant();

                    if (activePackageIds.Contains(depPackageIdLower)) continue;

                    if (inactiveModsLookup.TryGetValue(depPackageIdLower, out var dependencyMod))
                    {
                        if (processedDependencies.Contains(depPackageIdLower)) continue;

                        _stateTracker.Activate(dependencyMod);
                        _orderService.AddMod(dependencyMod, _orderService.VirtualActiveMods.Count);
                        addedMods.Add(dependencyMod);
                        activePackageIds.Add(depPackageIdLower);
                        modsToCheckQueue.Enqueue(dependencyMod);
                        processedDependencies.Add(depPackageIdLower);
                        listChanged = true;
                    }
                    else
                    {
                        string finalDisplayName = dependency.DisplayName ?? depPackageIdLower;
                        string? finalSteamUrl = dependency.SteamWorkshopUrl;

                        bool needsLookup = string.IsNullOrWhiteSpace(finalSteamUrl) || finalSteamUrl.Trim().StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

                        if (needsLookup)
                        {
                            var entry = _modDictionaryService.GetEntryByPackageId(depPackageIdLower);
                            if (entry != null && !string.IsNullOrWhiteSpace(entry.SteamId))
                            {
                                finalSteamUrl = $"https://steamcommunity.com/workshop/filedetails/?id={entry.SteamId}";
                                if (string.Equals(finalDisplayName, depPackageIdLower, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Name))
                                    finalDisplayName = entry.Name;
                            }
                            else if (needsLookup && !string.IsNullOrWhiteSpace(finalSteamUrl) && finalSteamUrl.StartsWith("steam://"))
                                finalSteamUrl = null;
                        }

                        if (missingDepsDict.TryGetValue(depPackageIdLower, out var existing))
                        {
                            if (!existing.requiredBy.Contains(currentMod.Name ?? "Unknown Mod"))
                                existing.requiredBy.Add(currentMod.Name ?? "Unknown Mod");
                            
                            if (string.IsNullOrWhiteSpace(existing.steamUrl) && !string.IsNullOrWhiteSpace(finalSteamUrl))
                                existing.steamUrl = finalSteamUrl;
                        }
                        else
                        {
                            missingDepsDict[depPackageIdLower] = (finalDisplayName, finalSteamUrl ?? "", new List<string> { currentMod.Name ?? "Unknown Mod" });
                        }
                        processedDependencies.Add(depPackageIdLower);
                    }
                }
            }

            var tempMissing = missingDepsDict.Select(kvp => (kvp.Value.displayName, kvp.Key, kvp.Value.steamUrl, kvp.Value.requiredBy)).ToList();
            if (listChanged) RaiseListChanged();
            return (addedMods, tempMissing);
        }

        private void RecalculateActiveModIssues()
        {
            var activeModsWithOrder = _orderService.VirtualActiveMods.ToList();
            if (!activeModsWithOrder.Any())
            {
                _hasAnyActiveModIssues = false;
                return;
            }

            var activeModLookup = new Dictionary<string, (ModItem Mod, int LoadOrder)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in activeModsWithOrder)
            {
                if (entry.Mod != null && !string.IsNullOrEmpty(entry.Mod.PackageId))
                    activeModLookup.TryAdd(entry.Mod.PackageId, entry);
            }

            var activePackageIds = activeModLookup.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool anyIssuesFound = false;

            foreach (var currentEntry in activeModsWithOrder)
            {
                var currentMod = currentEntry.Mod;
                if (currentMod == null) continue;

                var issues = new List<string>();
                currentMod.HasIssues = false;
                currentMod.IssueTooltipText = string.Empty;

                if (currentMod.ModDependencies != null)
                {
                    foreach (var dep in currentMod.ModDependencies)
                    {
                        if (string.IsNullOrEmpty(dep.PackageId)) continue;
                        var depIdLower = dep.PackageId.ToLowerInvariant();
                        if (!activePackageIds.Contains(depIdLower)) issues.Add($"Dependency missing: '{dep.DisplayName ?? dep.PackageId}'");
                        else if (activeModLookup.TryGetValue(depIdLower, out var depEntry) && depEntry.LoadOrder > currentEntry.LoadOrder)
                            issues.Add($"Dependency load order: '{dep.DisplayName ?? dep.PackageId}' must load before this mod.");
                    }
                }

                if (currentMod.IncompatibleWith != null)
                {
                    foreach (var incompatibleId in currentMod.IncompatibleWith.Keys)
                    {
                        if (string.IsNullOrWhiteSpace(incompatibleId)) continue;
                        if (activePackageIds.Contains(incompatibleId))
                        {
                            currentMod.IncompatibleWith.TryGetValue(incompatibleId, out var rule);
                            string name = activeModLookup.TryGetValue(incompatibleId, out var incEntry) ? incEntry.Mod?.Name ?? incompatibleId : incompatibleId;
                            var comment = rule?.Comment?.FirstOrDefault();
                            issues.Add($"Incompatible with active mod: '{name}'" + (string.IsNullOrWhiteSpace(comment) ? "" : $" (Reason: {comment})"));
                        }
                    }
                }

                var loadBeforeIds = (currentMod.LoadBefore ?? Enumerable.Empty<string>()).Concat(currentMod.ForceLoadBefore ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Select(id => id.ToLowerInvariant());
                foreach (var id in loadBeforeIds)
                {
                    if (activeModLookup.TryGetValue(id, out var target) && target.LoadOrder < currentEntry.LoadOrder)
                        issues.Add($"Load order: Should load before '{target.Mod?.Name ?? id}', but loads after.");
                }

                var loadAfterIds = (currentMod.LoadAfter ?? Enumerable.Empty<string>()).Concat(currentMod.ForceLoadAfter ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Select(id => id.ToLowerInvariant());
                foreach (var id in loadAfterIds)
                {
                    if (activeModLookup.TryGetValue(id, out var target) && target.LoadOrder > currentEntry.LoadOrder)
                        issues.Add($"Load order: Should load after '{target.Mod?.Name ?? id}', but loads before.");
                }

                if (issues.Count > 0)
                {
                    currentMod.HasIssues = true;
                    currentMod.IssueTooltipText = string.Join(Environment.NewLine, issues);
                    anyIssuesFound = true;
                }
            }
            _hasAnyActiveModIssues = anyIssuesFound;
        }
    }
}
