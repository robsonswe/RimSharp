using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Service for checking and applying workshop mod updates.
    /// Based on the researched community approach for workshop update queries.
    /// </summary>
    public interface IWorkshopUpdateService
    {
        /// <summary>
        /// Check for updates for workshop mods.
        /// Progress reports, one per mod as it is checked: (modsCheckedSoFar, totalMods, current mod's display name).
        /// </summary>
        /// <param name="mods">List of mods to check</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Update check results</returns>
        Task<WorkshopUpdateResult> CheckForUpdatesAsync(IEnumerable<ModItem> mods, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Update a specific workshop mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateModAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Update multiple workshop mods.
        /// </summary>
        /// <param name="steamIds">List of Steam Workshop IDs to update</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Update operation results</returns>
        Task<WorkshopUpdateBatchResult> UpdateModsAsync(IEnumerable<string> steamIds, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a workshop update check.
    /// </summary>
    public class WorkshopUpdateResult
    {
        public WorkshopUpdateStatus Status { get; set; }
        public int ModsChecked { get; set; }
        public int UpdatesAvailable { get; set; }
        public List<string> FailedModIds { get; set; } = new();
        public List<string> ErrorMessages { get; set; } = new();

        /// <summary>
        /// Per-mod decisions for every checked workshop mod that has a usable
        /// remote detail record — the caller decides what to do per decision
        /// (queue a SteamCMD re-download for Tracked mods, surface the Steam
        /// client flow for Workshop mods, report InitialSyncPossible, ...).
        /// </summary>
        public List<WorkshopUpdateCandidate> Updates { get; } = new();
    }

    /// <summary>
    /// One checked workshop mod with its remote detail record and the decision
    /// that was made from the timestamp comparison.
    /// </summary>
    public class WorkshopUpdateCandidate
    {
        public required ModItem Mod { get; init; }
        public required WorkshopModDetails Details { get; init; }

        /// <summary>Steam WebAPI time_updated of the detail record (unix seconds, 0 if absent).</summary>
        public long ExternalTimeUpdated { get; init; }

        /// <summary>Local record time: max(ACF manifest, timestamp.txt-derived UpdateDate) (unix seconds, 0 = none).</summary>
        public long InternalTime { get; init; }

        public WorkshopUpdateDecision Decision { get; init; }
    }

    /// <summary>
    /// Result of a batch update operation.
    /// </summary>
    public class WorkshopUpdateBatchResult
    {
        public int TotalMods { get; set; }
        public int SuccessfullyUpdated { get; set; }
        public int FailedToUpdate { get; set; }
        public List<string> SuccessfullyUpdatedIds { get; set; } = new();
        public List<string> FailedToUpdateIds { get; set; } = new();
        public List<string> ErrorMessages { get; set; } = new();
    }

    /// <summary>
    /// Status of an update check operation.
    /// </summary>
    public enum WorkshopUpdateStatus
    {
        Success,
        NoWorkshopMods,
        Partial,
        Failed
    }
}