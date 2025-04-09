#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop; // <<< ADDED for SteamCmdDownloadResult

namespace RimSharp.Shared.Services.Contracts
{
    public interface ISteamCmdService
    {
        // ... other properties and methods remain the same ...
        string? SteamCmdExePath { get; }
        bool IsSetupComplete { get; }
        string SteamCmdInstallPath { get; }
        string SteamCmdWorkshopContentPath { get; }
        Task<bool> CheckSetupAsync();
        Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD, showing the console window.
        /// Requires SteamCMD to be set up first (IsSetupComplete should be true).
        /// Parses the log file after completion to determine success/failure per item.
        /// </summary>
        /// <param name="itemsToDownload">A collection of DownloadItem objects.</param>
        /// <param name="validate">Whether to include the 'validate' parameter in the SteamCMD command.</param>
        /// <param name="showWindow">If true, shows the SteamCMD console window (now default behavior). Parameter might be removed in future.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A SteamCmdDownloadResult object detailing succeeded and failed items.</returns>
        Task<SteamCmdDownloadResult> DownloadModsAsync( // <<< CHANGED Return Type & Removed Progress
                IEnumerable<DownloadItem> itemsToDownload,
                bool validate,
                // bool showWindow = true, // Kept for compatibility, but behavior is fixed
                CancellationToken cancellationToken = default);

        Task<bool> ClearDepotCacheAsync();
        string GetSteamCmdPrefixPath();
        Task SetSteamCmdPrefixPathAsync(string prefixPath);
    }
}
