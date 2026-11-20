using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;

#nullable enable // Enable nullable context for the interface

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface ISteamApiClient
    {
        Task<SteamApiResponse?> GetFileDetailsAsync(string steamId, CancellationToken cancellationToken = default);
        Task<SteamCollectionDetailsResponse?> GetCollectionDetailsAsync(string collectionId, CancellationToken cancellationToken = default);
        Task<SteamApiResponse?> GetPublishedFileDetailsBatchAsync(IEnumerable<string> fileIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves the community display name ("persona name") for a SteamID64
        /// via the public profile XML endpoint (no Web API key required).
        /// Returns null if unavailable (offline, private/tombstoned profile, parse failure).
        /// </summary>
        Task<string?> GetPersonaNameAsync(string steamId64, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets details for multiple workshop mods. Batches into chunks of 300 with
        /// retry/backoff on transient failures; returns an empty list on terminal failure.
        /// </summary>
        Task<List<WorkshopModDetails>> GetModDetailsBatchAsync(IEnumerable<string> steamIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets details for a single workshop mod, or null when unavailable/invalid.
        /// </summary>
        Task<WorkshopModDetails?> GetModDetailsAsync(string steamId, CancellationToken cancellationToken = default);
    }
}


