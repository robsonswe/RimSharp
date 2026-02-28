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
        /// Gets a value indicating whether any mod in the current active list has detected issues
        /// (missing dependencies, incompatibilities, load order violations).
        /// </summary>
        bool HasAnyActiveModIssues { get; }

        int ActiveSortingIssuesCount { get; }
        int ActiveMissingDependenciesCount { get; }
        int ActiveIncompatibilitiesCount { get; }
        int ActiveOutdatedModsCount { get; }

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

        // --- ADD THESE NEW METHODS ---
        /// <summary>
        /// Ensures the specified mods are active and attempts to place them starting at the given index.
        /// Handles both activating inactive mods and moving already active mods.
        /// </summary>
        void ActivateModsAt(IEnumerable<ModItem> mods, int index);

        /// <summary>
        /// Deactivates the specified collection of mods. Core mods are skipped.
        /// </summary>
        void DeactivateMods(IEnumerable<ModItem> mods);

        /// <summary>
        /// Reorders a collection of already active mods to a new target index.
        /// Assumes all mods in the list are currently active.
        /// </summary>
        void ReorderMods(IEnumerable<ModItem> modsToMove, int targetIndex);

        /// <summary>
        /// Removes the specified mods from all internal lists (available, active, inactive).
        /// Returns result indicating impact on active list.
        /// </summary>
        ModRemovalResult RemoveMods(IEnumerable<ModItem> mods);
        
        /// <summary>
        /// Returns a list of active mods that depend on the specified package ID.
        /// </summary>
        List<ModItem> GetActiveModsDependingOn(string packageId);
    }
}
