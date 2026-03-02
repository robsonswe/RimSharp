#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IModDeletionService
    {
        /// <summary>

        /// </summary>
        /// <param name="path">The path to the directory to delete.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DeleteDirectoryRobustAsync(string path, CancellationToken ct = default);
    }
}

