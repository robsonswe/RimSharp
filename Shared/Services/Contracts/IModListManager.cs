using System;
using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public struct ModRemovalResult
    {
        public bool InstanceRemoved;
        public bool ActivePackageIdLost;
        
        public static readonly ModRemovalResult None = new() { InstanceRemoved = false, ActivePackageIdLost = false };
        public static readonly ModRemovalResult Success = new() { InstanceRemoved = true, ActivePackageIdLost = false };
        public static readonly ModRemovalResult Critical = new() { InstanceRemoved = true, ActivePackageIdLost = true };
    }

    public class ModListChangedEventArgs : EventArgs
    {
        public bool ActiveListModified { get; }
        public ModListChangedEventArgs(bool activeListModified)
        {
            ActiveListModified = activeListModified;
        }
    }

    public interface IModListManager
    {
        IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods { get; }
        IReadOnlyList<ModItem> AllInactiveMods { get; }
        event EventHandler<ModListChangedEventArgs> ListChanged;
        /// <summary>

        /// (missing dependencies, incompatibilities, load order violations).
        /// </summary>
        bool HasAnyActiveModIssues { get; }

        int ActiveSortingIssuesCount { get; }
        int ActiveMissingDependenciesCount { get; }
        int ActiveIncompatibilitiesCount { get; }
        int ActiveVersionMismatchCount { get; }
        int ActiveDuplicateIssuesCount { get; }
        string CurrentMajorGameVersion { get; }

        IEnumerable<ModIssue> GetActiveModIssues();

        void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds);
        void ActivateMod(ModItem mod);
        void ActivateModAt(ModItem mod, int index);
        void DeactivateMod(ModItem mod);
        void ReorderMod(ModItem mod, int newIndex);
        void ClearActiveList();
        bool SortActiveList(System.Threading.CancellationToken ct = default);
        bool IsModActive(ModItem mod);
        IEnumerable<ModItem> GetAllMods();

        (List<ModItem> addedMods, List<(string displayName, string packageId, string steamUrl, List<string> requiredBy)> missingDependencies)
        ResolveDependencies();
        /// <summary>

/// </summary>
        void ActivateModsAt(IEnumerable<ModItem> mods, int index);

        /// <summary>

        /// </summary>
        void DeactivateMods(IEnumerable<ModItem> mods);

        /// <summary>

/// </summary>
        void ReorderMods(IEnumerable<ModItem> modsToMove, int targetIndex);

        /// <summary>

        /// Returns result indicating impact on active list.
        /// </summary>
        ModRemovalResult RemoveMods(IEnumerable<ModItem> mods);
        
        /// <summary>

        /// </summary>
        List<ModItem> GetActiveModsDependingOn(string packageId);
    }
}


