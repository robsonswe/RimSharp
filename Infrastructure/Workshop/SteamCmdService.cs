#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Workshop; // <<< Keep for SteamCmdDownloadResult

namespace RimSharp.Infrastructure.Workshop

{
    /// <summary>
    /// Facade service that implements ISteamCmdService by coordinating between specialized components.
    /// </summary>
    public class SteamCmdService : ISteamCmdService
    {
        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly ISteamCmdDownloader _downloader;
        private readonly ISteamCmdFileSystem _fileSystem;

        public SteamCmdService(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            ISteamCmdDownloader downloader,
            ISteamCmdFileSystem fileSystem)
        {
            _pathService = pathService;
            _installer = installer;
            _downloader = downloader;
            _fileSystem = fileSystem;
            SetupStateChanged = delegate { };
        }


        // Forward properties to the path service
        public string? SteamCmdExePath => _pathService.SteamCmdExePath;
        public string SteamCmdInstallPath => _pathService.SteamCmdInstallPath;
        public string SteamCmdWorkshopContentPath => _pathService.SteamCmdWorkshopContentPath;
        public bool IsSetupComplete => _installer.CheckSetupAsync().GetAwaiter().GetResult();
        public event EventHandler<bool>? SetupStateChanged;

        /// <summary>
        /// Checks if SteamCMD is present at the configured location.
        /// </summary>
        public Task<bool> CheckSetupAsync() => _installer.CheckSetupAsync();

        /// <summary>
        /// Gets the currently configured prefix path for SteamCMD installation.
        /// </summary>
        public string GetSteamCmdPrefixPath() => _pathService.GetSteamCmdPrefixPath();

        /// <summary>
        /// Sets the prefix path for SteamCMD installation and saves it to configuration.
        /// </summary>
        public Task SetSteamCmdPrefixPathAsync(string prefixPath) =>
            _pathService.SetSteamCmdPrefixPathAsync(prefixPath);

        /// <summary>
        /// Downloads, extracts, and configures SteamCMD, including setting up the necessary symlink.
        /// </summary>
        public Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            _installer.SetupAsync(progress, cancellationToken);

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD.
        /// </summary>
        public Task<SteamCmdDownloadResult> DownloadModsAsync( // <<< CHANGED Return Type & Removed Progress
    IEnumerable<DownloadItem> itemsToDownload,
    bool validate,
    // bool showWindow = true, // Pass it along if interface keeps it
    CancellationToken cancellationToken = default) =>
    _downloader.DownloadModsAsync(itemsToDownload, validate, /*showWindow,*/ cancellationToken); // Pass updated params

        /// <summary>
        /// Clears the SteamCMD depot cache directory.
        /// </summary>
        public Task<bool> ClearDepotCacheAsync() => _fileSystem.ClearDepotCacheAsync();

        private void OnSetupStateChanged(bool isSetup)
        {
            SetupStateChanged?.Invoke(this, isSetup);
        }

    }
}