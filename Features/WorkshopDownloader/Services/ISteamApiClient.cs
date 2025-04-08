using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface ISteamApiClient
    {
        /// <summary>
        /// Fetches details for a published file (mod) from the Steam API.
        /// </summary>
        /// <param name="steamId">The Steam Workshop File ID.</param>
        /// <returns>The parsed API response, or null if the request fails or returns invalid data.</returns>
        Task<SteamApiResponse> GetFileDetailsAsync(string steamId);
    }
}
