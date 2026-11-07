using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Service for managing Steam workshop subscriptions.
    /// Based on RimSort's workshop management approach.
    /// </summary>
    public interface ISteamSubscriptionService
    {
        /// <summary>
        /// Subscribe to a workshop mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SubscribeAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Unsubscribe from a workshop mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UnsubscribeAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Update a workshop mod (trigger Steam to download latest version).
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> UpdateModAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get subscription status for a mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <returns>True if subscribed, false otherwise</returns>
        Task<bool> IsSubscribedAsync(string steamId);

        /// <summary>
        /// Get all subscribed workshop mods.
        /// </summary>
        /// <returns>List of subscribed mod IDs</returns>
        Task<List<string>> GetSubscribedModsAsync();

        /// <summary>
        /// Refresh subscription status from Steam.
        /// </summary>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of mods refreshed</returns>
        Task<int> RefreshSubscriptionsAsync(IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resubscribe to a list of mods (RimSort pattern: unsub all → 1s → sub all → 1s → DownloadItem).
        /// </summary>
        /// <param name="steamIds">List of Steam Workshop IDs</param>
        /// <param name="progress">Optional progress reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of mods successfully resubscribed</returns>
        Task<int> ResubscribeAsync(IEnumerable<string> steamIds, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get download progress for a mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID</param>
        /// <returns>Download progress info or null if not available</returns>
        Task<(bool isDownloading, ulong bytesDownloaded, ulong bytesTotal)?> GetDownloadProgressAsync(string steamId);

        /// <summary>
        /// Check if the current Steam account owns RimWorld (AppID 294100).
        /// This is the ownership gate for all workshop operations.
        /// </summary>
        /// <returns>True if the account owns RimWorld, false otherwise (including if Steamworks unavailable)</returns>
        Task<bool> CheckOwnershipAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Launch Steam's "Verify integrity of game files" for RimWorld (AppID 294100).
        /// Uses the steam://validate/294100 protocol handler.
        /// </summary>
        /// <returns>True if Steam was launched successfully, false otherwise</returns>
        Task<bool> VerifyGameFilesAsync();
    }
}