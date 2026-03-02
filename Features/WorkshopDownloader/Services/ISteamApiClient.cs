using System.Threading; // Add this using
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;

#nullable enable // Enable nullable context for the interface

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface ISteamApiClient
    {
        /// <summary>

        /// </summary>
        /// <param name="steamId">The Steam Workshop File ID.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>

        Task<SteamApiResponse?> GetFileDetailsAsync(string steamId, CancellationToken cancellationToken = default);
    }
}


