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
    }
}