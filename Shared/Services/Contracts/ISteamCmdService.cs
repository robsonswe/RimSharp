using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    public interface ISteamCmdService
    {
        /// <summary>
        /// Gets the full path to the SteamCMD executable. Returns null if not found or configured.
        /// </summary>
        string? SteamCmdExePath { get; }

        /// <summary>
        /// Gets a value indicating whether SteamCMD appears to be installed and configured correctly.
        /// </summary>
        bool IsSetupComplete { get; }

        /// <summary>
        /// Gets the configured installation directory for SteamCMD.
        /// </summary>
        string SteamCmdInstallPath { get; }

        /// <summary>
        /// Gets the path where SteamCMD expects Workshop content to be downloaded (used for symlinking).
        /// Typically %SteamCmdInstallPath%/steamapps/workshop/content/294100
        /// </summary>
        string SteamCmdWorkshopContentPath { get; }


        /// <summary>
        /// Checks if SteamCMD is present at the configured location. Updates IsSetupComplete.
        /// </summary>
        /// <returns>True if SteamCMD executable is found, false otherwise.</returns>
        Task<bool> CheckSetupAsync();

        /// <summary>
        /// Downloads, extracts, and configures SteamCMD, including setting up the necessary symlink.
        /// Requires the 'Mods Path' to be configured correctly via IPathService.
        /// </summary>
        /// <param name="progress">Optional progress reporter for status updates.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if setup completes successfully, false otherwise.</returns>
        Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD.
        /// Requires SteamCMD to be set up first (IsSetupComplete should be true).
        /// </summary>
        /// <param name="workshopIds">A collection of Workshop File IDs to download.</param>
        /// <param name="validate">Whether to include the 'validate' parameter in the SteamCMD command.</param>
        /// <param name="progress">Optional progress reporter for status updates and SteamCMD output.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the download process completes successfully (ExitCode 0), false otherwise.</returns>
        Task<bool> DownloadModsAsync(IEnumerable<string> workshopIds, bool validate, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the SteamCMD depot cache directory.
        /// </summary>
        /// <returns>True if the cache was cleared or didn't exist, false if an error occurred during deletion.</returns>
        Task<bool> ClearDepotCacheAsync();

        /// <summary>
        /// Gets the currently configured prefix path for SteamCMD installation.
        /// </summary>
        string GetSteamCmdPrefixPath();

        /// <summary>
        /// Sets the prefix path for SteamCMD installation and saves it to configuration.
        /// Also re-initializes internal paths.
        /// </summary>
        /// <param name="prefixPath">The new base path for SteamCMD.</param>
        Task SetSteamCmdPrefixPathAsync(string prefixPath);
    }
}
