#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Service for robustly deleting mod directories, handling common issues like read-only files.
    /// </summary>
    public interface IModDeletionService
    {
        /// <summary>
        /// Robustly deletes a directory and all its contents, clearing read-only attributes if necessary.
        /// </summary>
        /// <param name="path">The path to the directory to delete.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DeleteDirectoryRobustAsync(string path, CancellationToken ct = default);
    }
}
