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

        // --- State Tracking ---
        private bool _isSetupCompleteInternal; // Track internal state
        public event EventHandler<bool>? SetupStateChanged;
        // --------------------

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
            SetupStateChanged = delegate { }; // Initialize event

            // Initialize the internal state on creation (can be sync or async depending on preference)
            // Sync approach (less ideal but simple for constructor):
            // _isSetupCompleteInternal = _installer.CheckSetupAsync().GetAwaiter().GetResult();
            // Better: Leave false, let the first CheckSetupAsync call set it.
            _isSetupCompleteInternal = false;
        }

        // Forward properties to the path service
        public string? SteamCmdExePath => _pathService.SteamCmdExePath;
        public string SteamCmdInstallPath => _pathService.SteamCmdInstallPath;
        public string SteamCmdWorkshopContentPath => _pathService.SteamCmdWorkshopContentPath;

        // Property now returns the cached state
        public bool IsSetupComplete => _isSetupCompleteInternal;

        /// <summary>
        /// Checks if SteamCMD is present, updates internal state, and fires event if changed.
        /// </summary>
        public async Task<bool> CheckSetupAsync()
        {
            bool currentState = await _installer.CheckSetupAsync();
            // Only update and fire event if the state actually changed
            if (currentState != _isSetupCompleteInternal)
            {
                _isSetupCompleteInternal = currentState;
                OnSetupStateChanged(_isSetupCompleteInternal); // Fire the event
            }
            return currentState;
        }

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
        /// Downloads, extracts, and configures SteamCMD. Crucially, checks status afterwards.
        /// </summary>
        public async Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            bool success = await _installer.SetupAsync(progress, cancellationToken);
            // *** IMPORTANT: Re-check status after setup attempt ***
            // This will update _isSetupCompleteInternal and fire SetupStateChanged if needed.
            await CheckSetupAsync();
            return success;
        }

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD.
        /// </summary>
        public Task<SteamCmdDownloadResult> DownloadModsAsync(
            IEnumerable<DownloadItem> itemsToDownload,
            bool validate,
            CancellationToken cancellationToken = default) =>
            _downloader.DownloadModsAsync(itemsToDownload, validate, cancellationToken);

        /// <summary>
        /// Clears the SteamCMD depot cache directory.
        /// </summary>
        public Task<bool> ClearDepotCacheAsync() => _fileSystem.ClearDepotCacheAsync();

        // Helper method to invoke the event safely
        private void OnSetupStateChanged(bool isSetup)
        {
            SetupStateChanged?.Invoke(this, isSetup);
        }
    }
}
