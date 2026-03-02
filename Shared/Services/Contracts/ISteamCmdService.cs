// Shared/Services/Contracts/ISteamCmdService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop.Download.Models;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface ISteamCmdService
    {
        /// <summary>

        /// </summary>
        string? SteamCmdExePath { get; }

        /// <summary>

/// </summary>
        bool IsSetupComplete { get; }

        /// <summary>

        /// </summary>
        string SteamCmdInstallPath { get; }

        /// <summary>

/// </summary>
        string SteamCmdWorkshopContentPath { get; }

        /// <summary>

/// </summary>
        Task<bool> CheckSetupAsync();

        /// <summary>

        /// </summary>
        /// <param name="progress">Optional progress reporter for status updates.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>

        Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>

        /// </summary>
        event EventHandler<bool> SetupStateChanged;

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD.

        /// </summary>

/// <param name="progress">Optional progress reporter for status updates.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>

        Task<SteamCmdDownloadResult> DownloadModsAsync(
                IEnumerable<DownloadItem> itemsToDownload,
                bool validate,
                IProgress<(int current, int total, string message)>? progress = null,
                CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to clear the SteamCMD depot cache directory.
        /// </summary>

        Task<bool> ClearDepotCacheAsync();

        /// <summary>

        /// </summary>
        string GetSteamCmdPrefixPath();

        /// <summary>

        /// Re-initializes internal paths based on the new prefix.
        /// </summary>
        /// <param name="prefixPath">The new root directory for SteamCMD data.</param>
        Task SetSteamCmdPrefixPathAsync(string prefixPath);
    }
}

