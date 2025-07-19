#nullable enable
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines a service for checking for new versions of the game.
    /// </summary>
    public interface IUpdaterService
    {
        /// <summary>
        /// Checks for an update by comparing the local version against the latest public version from the API.
        /// </summary>
        /// <returns>A tuple indicating if an update is available and what the latest version string is.</returns>
        Task<(bool isUpdateAvailable, string? latestVersion)> CheckForUpdateAsync();
    }
}