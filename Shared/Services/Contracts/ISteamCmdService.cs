// Shared/Services/Contracts/ISteamCmdService.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop.Download.Models; // For DownloadItem

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Provides an interface for interacting with SteamCMD for installation, downloading, and path management.
    /// </summary>
    public interface ISteamCmdService
    {
        /// <summary>
        /// Gets the full path to the SteamCMD executable, or null if not configured/found.
        /// </summary>
        string? SteamCmdExePath { get; }

        /// <summary>
        /// Gets a value indicating whether SteamCMD appears to be set up correctly (executable found).
        /// Check this before attempting downloads. Listen to SetupStateChanged for updates.
        /// </summary>
        bool IsSetupComplete { get; }

        /// <summary>
        /// Gets the path where SteamCMD itself is installed (contains steamcmd.exe/sh, logs, etc.).
        /// </summary>
        string SteamCmdInstallPath { get; }

        /// <summary>
        /// Gets the path where SteamCMD downloads workshop content (e.g., .../steamapps/workshop/content/294100).
        /// This is the source directory for downloaded mods before processing.
        /// </summary>
        string SteamCmdWorkshopContentPath { get; }

        /// <summary>
        /// Checks if the SteamCMD executable exists at the configured path.
        /// Updates the internal state and raises SetupStateChanged if the state changes.
        /// </summary>
        Task<bool> CheckSetupAsync();

        /// <summary>
        /// Attempts to download and set up SteamCMD in the configured prefix path.
        /// </summary>
        /// <param name="progress">Optional progress reporter for status updates.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the setup process completed without critical errors AND CheckSetupAsync now returns true, false otherwise.</returns>
        Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when the setup state (IsSetupComplete) changes, typically after CheckSetupAsync or SetupAsync.
        /// </summary>
        event EventHandler<bool> SetupStateChanged;

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD.
        /// Requires SteamCMD to be set up first (IsSetupComplete should be true).
        /// </summary>
        /// <param name="itemsToDownload">A collection of DownloadItem objects representing the mods to download.</param>
        /// <param name="validate">Whether to include the 'validate' parameter in the SteamCMD command (performs integrity check).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A <see cref="SteamCmdDownloadResult"/> object detailing succeeded and failed items, log messages, and the exit code.</returns>
        Task<SteamCmdDownloadResult> DownloadModsAsync( // <<< Correct Return Type from Shared.Models
                IEnumerable<DownloadItem> itemsToDownload,
                bool validate,
                CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to clear the SteamCMD depot cache directory.
        /// </summary>
        /// <returns>True if the cache was cleared successfully or didn't exist, false on error.</returns>
        Task<bool> ClearDepotCacheAsync();

        /// <summary>
        /// Gets the currently configured prefix path where SteamCMD data (installation, downloads) is stored.
        /// </summary>
        string GetSteamCmdPrefixPath();

        /// <summary>
        /// Sets the prefix path for SteamCMD data storage and saves it to configuration.
        /// Re-initializes internal paths based on the new prefix.
        /// </summary>
        /// <param name="prefixPath">The new root directory for SteamCMD data.</param>
        Task SetSteamCmdPrefixPathAsync(string prefixPath);
    }
}