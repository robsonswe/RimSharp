using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Service for checking and applying workshop mod updates.
    /// Based on RimSort's query_workshop_update_data approach.
    /// </summary>
    public interface IWorkshopUpdateService
    {
        /// <summary>
        /// Check for updates for workshop mods.
        /// </summary>
        /// <param name="mods">List of mods to check</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Update check results</returns>
        Task<WorkshopUpdateResult> CheckForUpdatesAsync(IEnumerable<ModItem> mods, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get update information for a specific mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <returns>Update information or null if not available</returns>
        Task<ModUpdateInfo?> GetUpdateInfoAsync(string steamId);

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
        public int UpdatesApplied { get; set; }
        public List<string> FailedModIds { get; set; } = new();
        public List<string> ErrorMessages { get; set; } = new();
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

    /// <summary>
    /// Update information for a mod.
    /// </summary>
    public class ModUpdateInfo
    {
        public string SteamId { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public DateTime? LastUpdated { get; set; }
        public DateTime? LatestUpdate { get; set; }
        public bool UpdateAvailable { get; set; }
        public long? FileSize { get; set; }
    }
}