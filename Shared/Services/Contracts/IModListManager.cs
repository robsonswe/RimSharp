using System;
using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModListManager
    {
        IReadOnlyList<(ModItem Mod, int LoadOrder)> VirtualActiveMods { get; }
        IReadOnlyList<ModItem> AllInactiveMods { get; }
        event EventHandler ListChanged;
        void Initialize(IEnumerable<ModItem> allAvailableMods, IEnumerable<string> activeModPackageIds);
        void ActivateMod(ModItem mod);
        void ActivateModAt(ModItem mod, int index);
        void DeactivateMod(ModItem mod);
        void ReorderMod(ModItem mod, int newIndex);
        void ClearActiveList();
        bool SortActiveList();
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
    }
}
