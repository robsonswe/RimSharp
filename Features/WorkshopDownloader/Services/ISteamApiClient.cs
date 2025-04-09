using System.Threading; // Add this using
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;

#nullable enable // Enable nullable context for the interface

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface ISteamApiClient
    {
        /// <summary>
        /// Fetches details for a published file (mod) from the Steam API.
        /// </summary>
        /// <param name="steamId">The Steam Workshop File ID.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param> // <--- ADDED
        /// <returns>The parsed API response, or null if the request fails, is cancelled, or returns invalid data.</returns> // <--- Updated comment
        Task<SteamApiResponse?> GetFileDetailsAsync(string steamId, CancellationToken cancellationToken = default); // <--- ADDED parameter and nullable return type
    }
}