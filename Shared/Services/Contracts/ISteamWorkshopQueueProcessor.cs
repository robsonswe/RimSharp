using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines progress information reported by the SteamWorkshopQueueProcessor.
    /// </summary>
    public class QueueProcessProgress
    {
        public int CurrentItem { get; set; }
        public int TotalItems { get; set; }
        public string CurrentSteamId { get; set; }
        public string Message { get; set; }
        public string CurrentItemName { get; set; } // Added for better messages
    }

    /// <summary>
    /// Defines the result summary of the SteamWorkshopQueueProcessor operation.
    /// </summary>
    public class QueueProcessResult
    {
        public int TotalAttempted { get; set; }
        public int SuccessfullyAdded { get; set; }
        public int AlreadyQueued { get; set; }
        public int FailedProcessing { get; set; } // Combined API/Queue add failures
        public List<string> ErrorMessages { get; } = new List<string>();
        public List<string> AddedModNames { get; } = new List<string>();
        public bool WasCancelled { get; set; }
    }

    /// <summary>
    /// Service responsible for processing a list of Steam Workshop item IDs,
    /// checking their validity via the Steam API, and adding valid items to the download queue.
    /// </summary>
    public interface ISteamWorkshopQueueProcessor
    {
        /// <summary>
        /// Processes a list of Steam IDs, checks them against the Steam API, and queues valid ones for download.
        /// </summary>
        /// <param name="steamIds">The collection of Steam Workshop File IDs to process.</param>
        /// <param name="progress">Optional reporter for progress updates.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A summary of the processing results.</returns>
        Task<QueueProcessResult> ProcessAndEnqueueModsAsync(
            IEnumerable<string> steamIds,
            IProgress<QueueProcessProgress> progress,
            CancellationToken cancellationToken);
    }
}
