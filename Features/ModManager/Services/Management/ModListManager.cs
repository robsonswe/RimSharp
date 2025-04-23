using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimSharp.Infrastructure.Mods.Sorting;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts; // Ensure this is included

namespace RimSharp.Features.ModManager.Services.Management
{
    public class ModListManager : IModListManager
    {
        private readonly ModStateTracker _stateTracker;
        private readonly ModOrderService _orderService;
        private readonly ModDependencySorter _dependencySorter;
        private readonly ModLookupService _lookupService;
        private readonly IModDictionaryService _modDictionaryService; // Service for dictionary lookups
        private readonly List<ModItem> _allAvailableMods = new();
        private bool _hasAnyActiveModIssues = false;

        public event EventHandler ListChanged;

        // Constructor requires IModDictionaryService
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

        // --- Single Item Methods ---
        public void ActivateMod(ModItem mod) => ActivateModsAt(new[] { mod }, _orderService.VirtualActiveMods.Count);
        public void ActivateModAt(ModItem mod, int index) => ActivateModsAt(new[] { mod }, index);
        public void DeactivateMod(ModItem mod) => DeactivateMods(new[] { mod });
        public void ReorderMod(ModItem mod, int newIndex) => ReorderMods(new[] { mod }, newIndex);

        // --- Existing Methods ---
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


        public bool SortActiveList()
        {
            var currentActiveMods = _orderService.VirtualActiveMods.Select(x => x.Mod).ToList();
            if (currentActiveMods.Count <= 1) return false;

            var originalOrderIds = currentActiveMods
                .Select(mod => mod?.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            var hasExplicitLoadBefore = new HashSet<ModItem>();
            var hasExplicitForceLoadBefore = new HashSet<ModItem>();
            foreach (var mod in currentActiveMods)
            {
                if (mod == null) continue;
                if (mod.LoadBefore?.Count > 0 || mod.ForceLoadBefore?.Count > 0) hasExplicitLoadBefore.Add(mod);
                if (mod.ForceLoadBefore?.Count > 0) hasExplicitForceLoadBefore.Add(mod);
            }

            var sortedMods = _dependencySorter.TopologicalSort(currentActiveMods, hasExplicitLoadBefore, hasExplicitForceLoadBefore);
            if (sortedMods == null || sortedMods.Count != currentActiveMods.Count)
            {
                Debug.WriteLine("Sorting failed or resulted in a different number of mods (cycle?).");
                return false;
            }

            var newOrderIds = sortedMods
                .Select(mod => mod?.PackageId?.ToLowerInvariant() ?? Guid.NewGuid().ToString())
                .ToList();

            bool orderChanged = !originalOrderIds.SequenceEqual(newOrderIds);

            if (orderChanged)
            {
                _orderService.Clear();
                _orderService.AddModsAt(sortedMods, 0);
                RaiseListChanged();
                Debug.WriteLine("Active mod list sorted successfully. Order changed.");
            }
            else
            {
                Debug.WriteLine("Sorting completed. Mod order sequence unchanged.");
            }

            return orderChanged;
        }

        public bool IsModActive(ModItem mod) => _stateTracker.IsModActive(mod);

        public IEnumerable<ModItem> GetAllMods() => _allAvailableMods;

        private void RaiseListChanged()
        {
            RecalculateActiveModIssues();
            Debug.WriteLine("ModListManager: Raising ListChanged event.");
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        // --- IMPLEMENTATION OF BULK METHODS ---

        public void ActivateModsAt(IEnumerable<ModItem> mods, int index)
        {
            if (mods == null) return;
            bool listChanged = false;

            var modsToProcess = mods.Where(m => m != null).Distinct().ToList();
            if (!modsToProcess.Any()) return;

            Debug.WriteLine($"ModListManager: ActivateModsAt called for {modsToProcess.Count} mods at index {index}.");

            var modsToActivate = modsToProcess.Where(m => !_stateTracker.IsModActive(m)).ToList();
            var modsToReorder = modsToProcess.Where(m => _stateTracker.IsModActive(m)).ToList();

            if (modsToReorder.Any())
            {
                Debug.WriteLine($"ActivateModsAt: Reordering {modsToReorder.Count} already active mods.");
                bool reorderChanged = _orderService.ReorderMods(modsToReorder, index);
                listChanged |= reorderChanged;
                var lastReorderedMod = modsToReorder.LastOrDefault(m => _orderService.VirtualActiveMods.Any(vm => vm.Mod == m));
                if (lastReorderedMod != null)
                {
                    index = _orderService.VirtualActiveMods.ToList().FindIndex(vm => vm.Mod == lastReorderedMod) + 1;
                }
                index = Math.Clamp(index, 0, _orderService.VirtualActiveMods.Count);
            }

            if (modsToActivate.Any())
            {
                Debug.WriteLine($"ActivateModsAt: Activating {modsToActivate.Count} new mods.");
                foreach (var mod in modsToActivate)
                {
                    _stateTracker.Activate(mod);
                }
                bool added = _orderService.AddModsAt(modsToActivate, index);
                listChanged |= added;
            }

            if (listChanged)
            {
                RaiseListChanged();
            }
            else
            {
                Debug.WriteLine($"ModListManager: ActivateModsAt resulted in no changes.");
            }
        }


        public void DeactivateMods(IEnumerable<ModItem> mods)
        {
            if (mods == null) return;
            bool listChanged = false;

            var modsToDeactivate = mods
                .Where(m => m != null && m.ModType != ModType.Core)
                .Distinct()
                .ToList();

            if (!modsToDeactivate.Any())
            {
                Debug.WriteLine($"ModListManager: No valid mods to deactivate.");
                return;
            }

            Debug.WriteLine($"ModListManager: DeactivateMods called for {modsToDeactivate.Count} mods.");

            foreach (var mod in modsToDeactivate)
            {
                _stateTracker.Deactivate(mod);
            }

            bool removed = _orderService.RemoveMods(modsToDeactivate);
            listChanged = removed;

            if (listChanged)
            {
                RaiseListChanged();
            }
            else
            {
                Debug.WriteLine($"ModListManager: DeactivateMods resulted in no changes.");
            }
        }


        public void ReorderMods(IEnumerable<ModItem> modsToMove, int targetIndex)
        {
            if (modsToMove == null) return;

            var validModsToMove = modsToMove
                .Where(m => m != null && _stateTracker.IsModActive(m))
                .Distinct()
                .ToList();

            if (!validModsToMove.Any())
            {
                Debug.WriteLine($"ModListManager: No valid active mods provided for ReorderMods.");
                return;
            }

            Debug.WriteLine($"ModListManager: ReorderMods called for {validModsToMove.Count} mods to target index {targetIndex}.");

            bool changed = _orderService.ReorderMods(validModsToMove, targetIndex);

            if (changed)
            {
                RaiseListChanged();
            }
            else
            {
                Debug.WriteLine($"ModListManager: ReorderMods resulted in no change in order.");
            }
        }

        // --- Resolve Dependencies with Dictionary Lookup & steam:// Fix ---
        public (List<ModItem> addedMods, List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)> missingDependencies) ResolveDependencies()
        {
            var addedMods = new List<ModItem>();
            var tempMissingDependencies = new List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)>();
            bool listChanged = false;

            var activeModsForCheck = _orderService.VirtualActiveMods
                .Select(x => x.Mod)
                .ToList();

            var inactiveModsLookup = _stateTracker.AllInactiveMods.ToDictionary(m => m.PackageId, m => m, StringComparer.OrdinalIgnoreCase);
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
                    if (string.IsNullOrWhiteSpace(dependency.PackageId))
                    {
                        Debug.WriteLine($"[ResolveDependencies] Skipping dependency for '{currentMod.Name}' because PackageId is missing.");
                        continue;
                    }

                    var depPackageIdLower = dependency.PackageId.ToLowerInvariant(); // Outer declaration

                    if (activePackageIds.Contains(depPackageIdLower) || processedDependencies.Contains(depPackageIdLower))
                        continue;

                    processedDependencies.Add(depPackageIdLower);

                    if (inactiveModsLookup.TryGetValue(depPackageIdLower, out var dependencyMod))
                    {
                        Debug.WriteLine($"[ResolveDependencies] Found inactive dependency '{dependencyMod.Name}' ({depPackageIdLower}) required by '{currentMod.Name}'. Activating.");
                        _stateTracker.Activate(dependencyMod);
                        _orderService.AddMod(dependencyMod, _orderService.VirtualActiveMods.Count);

                        addedMods.Add(dependencyMod);
                        activePackageIds.Add(depPackageIdLower);
                        modsToCheckQueue.Enqueue(dependencyMod);
                        listChanged = true;
                    }
                    else // Dependency not found in inactive list, treat as missing
                    {
                        // Get initial values from the dependency definition in the requiring mod's About.xml
                        // Use the depPackageIdLower declared in the outer scope
                        string initialDisplayName = dependency.DisplayName ?? depPackageIdLower;
                        string initialSteamUrl = dependency.SteamWorkshopUrl;

                        // Initialize final values, starting with the initial ones
                        string finalDisplayName = initialDisplayName;
                        string finalSteamUrl = initialSteamUrl; // This might be null, whitespace, steam://, or https://

                        // --- Force dictionary lookup for missing or invalid URLs ---
                        bool needsDictionaryLookup = false;

                        // Condition 1: URL is completely missing from About.xml
                        if (string.IsNullOrWhiteSpace(initialSteamUrl))
                        {
                            Debug.WriteLine($"[ResolveDependencies] Dependency '{finalDisplayName}' ({depPackageIdLower}) is missing SteamWorkshopUrl in About.xml. Flagging for dictionary lookup.");
                            needsDictionaryLookup = true;
                            finalSteamUrl = null; // Ensure we start fresh if lookup occurs
                        }
                        // Condition 2: URL from About.xml exists but is the invalid 'steam://' format
                        else if (initialSteamUrl.Trim().StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"[ResolveDependencies] Dependency '{finalDisplayName}' ({depPackageIdLower}) has invalid 'steam://' URL ('{initialSteamUrl}') in About.xml. Discarding it and flagging for dictionary lookup.");
                            needsDictionaryLookup = true;
                            finalSteamUrl = null; // Discard the bad URL, prioritize dictionary result
                        }

                        // Perform dictionary lookup if needed
                        if (needsDictionaryLookup)
                        {
                            var dictionaryEntry = _modDictionaryService.GetEntryByPackageId(depPackageIdLower); // Use prioritized lookup

                            if (dictionaryEntry != null && !string.IsNullOrWhiteSpace(dictionaryEntry.SteamId))
                            {
                                // Construct the correct HTTPS URL from the dictionary entry.
                                string constructedUrl = $"https://steamcommunity.com/workshop/filedetails/?id={dictionaryEntry.SteamId}";
                                finalSteamUrl = constructedUrl; // Update finalSteamUrl with the good one

                                // Optionally update display name if it was missing/generic and dictionary has one
                                if (string.Equals(finalDisplayName, depPackageIdLower, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(dictionaryEntry.Name))
                                {
                                    finalDisplayName = dictionaryEntry.Name;
                                }
                                Debug.WriteLine($"[ResolveDependencies] Dictionary lookup successful for '{depPackageIdLower}'. Using SteamID: {dictionaryEntry.SteamId}, Constructed URL: {finalSteamUrl}");
                            }
                            else
                            {
                                Debug.WriteLine($"[ResolveDependencies] Dictionary lookup failed for '{depPackageIdLower}' or entry has no SteamID. Final URL remains null.");
                                // finalSteamUrl remains null if lookup failed after discarding steam:// or missing URL.
                            }
                        }
                        // --- End Force dictionary lookup ---


                        // Now add or update the missing dependency using the potentially corrected finalDisplayName and finalSteamUrl
                        Debug.WriteLine($"[ResolveDependencies] Marking dependency '{finalDisplayName}' ({depPackageIdLower}) as missing, required by '{currentMod.Name}'. Final URL: '{finalSteamUrl ?? "None"}'");

                        if (missingDepsDict.TryGetValue(depPackageIdLower, out var existingMissing))
                        {
                            if (!existingMissing.requiredBy.Contains(currentMod.Name ?? "Unknown Mod"))
                            {
                                existingMissing.requiredBy.Add(currentMod.Name ?? "Unknown Mod");
                            }
                            // Update URL/Name if this iteration found a better one (prioritize the HTTPS one)
                            if ((string.IsNullOrWhiteSpace(existingMissing.steamUrl) && !string.IsNullOrWhiteSpace(finalSteamUrl)) ||
                                (!string.IsNullOrWhiteSpace(finalSteamUrl) && finalSteamUrl.StartsWith("https://") && !(existingMissing.steamUrl?.StartsWith("https://") ?? false)))
                            {
                                existingMissing.steamUrl = finalSteamUrl;
                            }
                            if (string.Equals(existingMissing.displayName, depPackageIdLower, StringComparison.OrdinalIgnoreCase) && !string.Equals(finalDisplayName, depPackageIdLower, StringComparison.OrdinalIgnoreCase))
                            {
                                existingMissing.displayName = finalDisplayName;
                            }
                        }
                        else
                        {
                            missingDepsDict[depPackageIdLower] = (
                                displayName: finalDisplayName,
                                steamUrl: finalSteamUrl, // Use potentially updated URL
                                requiredBy: new List<string> { currentMod.Name ?? "Unknown Mod" }
                            );
                        }
                    }
                }
            }

            tempMissingDependencies = missingDepsDict.Select(kvp => (
                kvp.Value.displayName,
                kvp.Key, // packageId
                kvp.Value.steamUrl,
                kvp.Value.requiredBy
            )).ToList();

            if (listChanged)
            {
                // Optional: SortActiveList();
                RaiseListChanged();
            }

            return (addedMods, tempMissingDependencies);
        }

        // --- Recalculate Active Mod Issues ---
        private void RecalculateActiveModIssues()
        {
            var activeModsWithOrder = _orderService.VirtualActiveMods.ToList();
            if (!activeModsWithOrder.Any())
            {
                _hasAnyActiveModIssues = false;
                return;
            }

            var activeModLookup = activeModsWithOrder
                .Where(m => m.Mod != null && !string.IsNullOrEmpty(m.Mod.PackageId))
                .ToDictionary(m => m.Mod.PackageId.ToLowerInvariant(), m => m, StringComparer.OrdinalIgnoreCase);

            var activePackageIds = activeModLookup.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool anyIssuesFound = false;

            foreach (var currentEntry in activeModsWithOrder)
            {
                var currentMod = currentEntry.Mod;
                if (currentMod == null) continue;

                var issues = new List<string>();
                currentMod.HasIssues = false;
                currentMod.IssueTooltipText = string.Empty;

                // 1. Check Missing Dependencies
                if (currentMod.ModDependencies != null)
                {
                    foreach (var dep in currentMod.ModDependencies)
                    {
                        if (string.IsNullOrEmpty(dep.PackageId)) continue;
                        var depIdLower = dep.PackageId.ToLowerInvariant();
                        if (!activePackageIds.Contains(depIdLower))
                        {
                            issues.Add($"Dependency missing: '{dep.DisplayName ?? dep.PackageId}'");
                        }
                        // 2. Check Dependency Load Order
                        else if (activeModLookup.TryGetValue(depIdLower, out var depEntry))
                        {
                            if (depEntry.LoadOrder > currentEntry.LoadOrder)
                            {
                                issues.Add($"Dependency load order: '{dep.DisplayName ?? dep.PackageId}' must load before this mod.");
                            }
                        }
                    }
                }

                // 3. Check Incompatibilities
                if (currentMod.IncompatibleWith != null)
                {
                    foreach (var incompatibleId in currentMod.IncompatibleWith)
                    {
                        if (string.IsNullOrEmpty(incompatibleId)) continue;
                        var incIdLower = incompatibleId.ToLowerInvariant();
                        if (activePackageIds.Contains(incIdLower))
                        {
                            string incompatibleName = activeModLookup.TryGetValue(incIdLower, out var incEntry)
                                                    ? incEntry.Mod?.Name ?? incIdLower
                                                    : incIdLower;
                            issues.Add($"Incompatible with active mod: '{incompatibleName}'");
                        }
                    }
                }

                // 4. Check Load Before Order
                var loadBeforeIds = (currentMod.LoadBefore ?? Enumerable.Empty<string>())
                                   .Concat(currentMod.ForceLoadBefore ?? Enumerable.Empty<string>())
                                   .Where(id => !string.IsNullOrEmpty(id))
                                   .Select(id => id.ToLowerInvariant());
                foreach (var beforeIdLower in loadBeforeIds)
                {
                    if (activeModLookup.TryGetValue(beforeIdLower, out var targetEntry))
                    {
                        if (targetEntry.LoadOrder < currentEntry.LoadOrder)
                        {
                            string targetName = targetEntry.Mod?.Name ?? beforeIdLower;
                            issues.Add($"Load order: Should load before '{targetName}', but loads after.");
                        }
                    }
                }

                // 5. Check Load After Order
                var loadAfterIds = (currentMod.LoadAfter ?? Enumerable.Empty<string>())
                                   .Concat(currentMod.ForceLoadAfter ?? Enumerable.Empty<string>())
                                   .Where(id => !string.IsNullOrEmpty(id))
                                   .Select(id => id.ToLowerInvariant());
                foreach (var afterIdLower in loadAfterIds)
                {
                    if (activeModLookup.TryGetValue(afterIdLower, out var targetEntry))
                    {
                        if (targetEntry.LoadOrder > currentEntry.LoadOrder)
                        {
                            string targetName = targetEntry.Mod?.Name ?? afterIdLower;
                            issues.Add($"Load order: Should load after '{targetName}', but loads before.");
                        }
                    }
                }


                // Update mod properties if issues were found
                if (issues.Count > 0)
                {
                    currentMod.HasIssues = true;
                    currentMod.IssueTooltipText = string.Join(Environment.NewLine, issues);
                    anyIssuesFound = true;
                }
            }

            _hasAnyActiveModIssues = anyIssuesFound;
            Debug.WriteLine($"[RecalculateActiveModIssues] Finished. AnyIssuesFound: {_hasAnyActiveModIssues}");
        }
    }
}