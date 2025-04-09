using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

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
        }

        // Forward properties to the path service
        public string? SteamCmdExePath => _pathService.SteamCmdExePath;
        public string SteamCmdInstallPath => _pathService.SteamCmdInstallPath;
        public string SteamCmdWorkshopContentPath => _pathService.SteamCmdWorkshopContentPath;
        public bool IsSetupComplete => _installer.CheckSetupAsync().GetAwaiter().GetResult();

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
        public Task<bool> DownloadModsAsync(
            IEnumerable<string> workshopIds, 
            bool validate, 
            IProgress<string>? progress = null, 
            CancellationToken cancellationToken = default) => 
            _downloader.DownloadModsAsync(workshopIds, validate, progress, cancellationToken);

        /// <summary>
        /// Clears the SteamCMD depot cache directory.
        /// </summary>
        public Task<bool> ClearDepotCacheAsync() => _fileSystem.ClearDepotCacheAsync();
    }
}