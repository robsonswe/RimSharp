#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download;
using RimSharp.Infrastructure.Workshop.Download.Models;
namespace RimSharp.Infrastructure.Workshop
{
    public class SteamCmdService : ISteamCmdService
    {
        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly ISteamCmdDownloader _downloader;
        private readonly ISteamCmdFileSystem _fileSystem;

        private bool _isSetupCompleteInternal;
        public event EventHandler<bool>? SetupStateChanged;

        public SteamCmdService(
            ISteamCmdPathService pathService,   // Inject Core interface
            ISteamCmdInstaller installer,     // Inject Core interface
            ISteamCmdDownloader downloader,    // Inject Download interface
            ISteamCmdFileSystem fileSystem     // Inject Core interface
            )
        {
            _pathService = pathService;
            _installer = installer;
            _downloader = downloader;
            _fileSystem = fileSystem;
            SetupStateChanged = delegate { };
            _isSetupCompleteInternal = false;
        }

        // Properties using Core PathService
        public string? SteamCmdExePath => _pathService.SteamCmdExePath;
        public string SteamCmdInstallPath => _pathService.SteamCmdInstallPath;
        public string SteamCmdWorkshopContentPath => _pathService.SteamCmdWorkshopContentPath;

        public bool IsSetupComplete => _isSetupCompleteInternal;

        // Method using Core Installer
        public async Task<bool> CheckSetupAsync()
        {
            bool currentState = await _installer.CheckSetupAsync();
            if (currentState != _isSetupCompleteInternal)
            {
                _isSetupCompleteInternal = currentState;
                SetupStateChanged?.Invoke(this, _isSetupCompleteInternal);
            }
            return currentState;
        }

        // Method using Core PathService
        public string GetSteamCmdPrefixPath() => _pathService.GetSteamCmdPrefixPath();

        // Method using Core PathService
        public Task SetSteamCmdPrefixPathAsync(string prefixPath) =>
            _pathService.SetSteamCmdPrefixPathAsync(prefixPath);

        // Method using Core Installer
        public async Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            bool success = await _installer.SetupAsync(progress, cancellationToken);
            await CheckSetupAsync(); // Re-check status after attempt
            return success;
        }

        // Method using Download Downloader
        public Task<SteamCmdDownloadResult> DownloadModsAsync(
            IEnumerable<DownloadItem> itemsToDownload,
            bool validate,
            IProgress<(int current, int total, string message)>? progress = null,
            CancellationToken cancellationToken = default) =>
            _downloader.DownloadModsAsync(itemsToDownload, validate, progress, cancellationToken);

        // Method using Core FileSystem
        public Task<bool> ClearDepotCacheAsync() => _fileSystem.ClearDepotCacheAsync();
    }
}


