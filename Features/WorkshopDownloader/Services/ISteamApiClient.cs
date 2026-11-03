using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;

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
    }
}


