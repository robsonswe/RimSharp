#nullable enable
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IUpdaterService
    {
        /// <summary>

        /// </summary>

        Task<(bool isUpdateAvailable, string? latestVersion)> CheckForUpdateAsync();
    }
}
