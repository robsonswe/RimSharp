#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; // For DownloadItem

namespace RimSharp.Infrastructure.Workshop.Download.Processing
{
    public interface IDownloadedItemProcessor
    {
        /// <summary>
        /// Processes a single downloaded item: validates source, creates timestamps,
        /// handles backups, moves/copies to target, cleans up backup, and handles rollback on error.
        /// </summary>
        /// <param name="item">The item metadata.</param>
        /// <param name="sourcePath">The path where SteamCMD downloaded the item's content.</param>
        /// <param name="targetPath">The final destination path in the user's mods directory.</param>
        /// <param name="backupSuffix">The suffix to use for backup directories.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the item was processed successfully, false otherwise.</returns>
        Task<(bool Success, string Reason)> ProcessItemAsync(
            DownloadItem item,
            string sourcePath,
            string targetPath,
            string backupSuffix,
            CancellationToken cancellationToken);

        /// <summary>
        /// Adds a log message to the processing context (optional, could be handled via ILoggerService).
        /// Helps decouple logging details from the processor if needed elsewhere.
        /// </summary>
        /// <param name="message">The log message.</param>
        void AddLogMessage(string message); // Or remove if direct logging is preferred

         /// <summary>
        /// Retrieves accumulated log messages if AddLogMessage was used.
        /// </summary>
        /// <returns>List of log messages.</returns>
        List<string> GetLogMessages(); // Or remove if direct logging is preferred
    }
}