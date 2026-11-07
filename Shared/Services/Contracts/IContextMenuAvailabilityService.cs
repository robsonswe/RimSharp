using System.Collections;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Computes which context menu actions are available for the current multi-selection.
    /// An action is only available if it applies to ALL selected mods (intersection semantics).
    /// </summary>
    public interface IContextMenuAvailabilityService
    {
        /// <summary>
        /// Gets whether the selection has at least one mod.
        /// </summary>
        bool HasSelection { get; }

        /// <summary>
        /// Gets whether the "Delete Mod" action is available for all selected mods.
        /// Available for Tracked, Untracked, Git mods. NOT for Core, Expansion, Workshop.
        /// </summary>
        bool CanDeleteMod { get; }

        /// <summary>
        /// Gets whether the "Delete and Unsubscribe" action is available for all selected mods.
        /// Only available for Workshop mods.
        /// </summary>
        bool CanDeleteAndUnsubscribe { get; }

        /// <summary>
        /// Gets whether the "Delete and Resubscribe" action is available for all selected mods.
        /// Only available for Workshop mods.
        /// </summary>
        bool CanDeleteAndResubscribe { get; }

        /// <summary>
        /// Gets whether the "Unsubscribe" action is available for all selected mods.
        /// Only available for Workshop mods.
        /// </summary>
        bool CanUnsubscribe { get; }

        /// <summary>
        /// Gets whether the "Redownload Mod" action is available for all selected mods.
        /// Only available for Tracked mods.
        /// </summary>
        bool CanRedownloadMod { get; }

        /// <summary>
        /// Gets whether the "Check for Updates" action is available for all selected mods.
        /// Only available for Workshop mods.
        /// </summary>
        bool CanCheckForUpdates { get; }

        /// <summary>
        /// Gets whether the "Update Mod" action is available for all selected mods.
        /// Only available for Workshop mods.
        /// </summary>
        bool CanUpdateMod { get; }

        /// <summary>
        /// Gets whether the "Subscribe" action is available for all selected mods.
        /// Only available for non-Workshop, non-Core, non-Expansion mods that have a Steam URL.
        /// </summary>
        bool CanSubscribe { get; }

        /// <summary>
        /// Gets whether the "Open Folder" action is available for all selected mods.
        /// Available for mods with a valid local path.
        /// </summary>
        bool CanOpenFolder { get; }

        /// <summary>
        /// Gets whether the "Open Workshop Page" action is available for all selected mods.
        /// Only available for Workshop mods or mods with a Steam URL.
        /// </summary>
        bool CanOpenWorkshopPage { get; }

        /// <summary>
        /// Gets whether the "Open External URL" action is available for all selected mods.
        /// Available for mods with an external URL.
        /// </summary>
        bool CanOpenExternalUrl { get; }

        /// <summary>
        /// Gets whether the "AI Conflict Scan" action is available for all selected mods.
        /// Available for all mod types except Core/Expansion.
        /// </summary>
        bool CanAiConflictScan { get; }

        /// <summary>
        /// Gets whether the "Customize Mod" action is available for all selected mods.
        /// Available for all mod types except Core/Expansion.
        /// </summary>
        bool CanCustomizeMod { get; }

        /// <summary>
        /// Gets whether the "Verify Game Files" action is available.
        /// Only available for Steam installations (gated on GameInstance.IsSteamInstallation).
        /// </summary>
        bool CanVerifyGameFiles { get; }

        /// <summary>
        /// Updates the current selection and recomputes availability.
        /// </summary>
        void UpdateSelection(IList? selectedItems);
    }
}