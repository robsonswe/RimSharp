#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Infrastructure.Workshop.Download.Processing
{
    public interface IDownloadedItemProcessor
    {
        /// <summary>
        /// Processes a single downloaded item: validates source, creates timestamps,
        /// handles backups, moves/copies to target, cleans up backup, and handles rollback on error.
        /// </summary>
        Task<(bool Success, string Reason)> ProcessItemAsync(
            DownloadItem item,
            string sourcePath,
            string targetPath,
            string backupSuffix,
            CancellationToken cancellationToken);

        void AddLogMessage(string message); 
        List<string> GetLogMessages(); 
    }
}
