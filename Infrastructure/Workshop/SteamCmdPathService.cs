#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop
{
    /// <summary>
    /// Manages paths related to SteamCMD installation and operation.
    /// </summary>
    public class SteamCmdPathService : ISteamCmdPathService
    {
        private const string SteamCmdPrefixConfigKey = "steamcmd_prefix";
        private const string RimworldAppId = "294100";

        private readonly IConfigService _configService;
        private string _steamCmdExeName;

        private string _steamCmdPrefixPath = string.Empty;
        private string _steamCmdInstallPath = string.Empty;
        private string _steamCmdSteamAppsPath = string.Empty;
        private string _steamCmdWorkshopContentPath = string.Empty;
        private string _steamCmdDepotCachePath = string.Empty;
        private string? _steamCmdExePath;

        public string? SteamCmdExePath => _steamCmdExePath;
        public string SteamCmdInstallPath => _steamCmdInstallPath;
        public string SteamCmdWorkshopContentPath => _steamCmdWorkshopContentPath;
        public string SteamCmdPrefixPath => _steamCmdPrefixPath;
        public string SteamCmdDepotCachePath => _steamCmdDepotCachePath;
        public string SteamCmdSteamAppsPath => _steamCmdSteamAppsPath;

        public SteamCmdPathService(IConfigService configService, string steamCmdExeName)
        {
            _configService = configService;
            _steamCmdExeName = steamCmdExeName;
            InitializePaths();
        }

        public void InitializePaths()
        {
            _steamCmdPrefixPath = _configService.GetConfigValue(SteamCmdPrefixConfigKey);
            if (string.IsNullOrWhiteSpace(_steamCmdPrefixPath))
            {
                // Default path (e.g., AppData or next to executable)
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                _steamCmdPrefixPath = Path.Combine(basePath, "SteamCMD_Data");
                // Don't save default automatically, wait for explicit setup/config change
            }

            // Define core paths based on the prefix
            _steamCmdInstallPath = Path.Combine(_steamCmdPrefixPath, "steamcmd");
            _steamCmdSteamAppsPath = Path.Combine(_steamCmdPrefixPath, "steam"); // Where 'force_install_dir' points
            _steamCmdWorkshopContentPath = Path.Combine(_steamCmdSteamAppsPath, "steamapps", "workshop", "content", RimworldAppId);
            _steamCmdDepotCachePath = Path.Combine(_steamCmdInstallPath, "depotcache");
            _steamCmdExePath = Path.Combine(_steamCmdInstallPath, _steamCmdExeName);
        }

        public string GetSteamCmdPrefixPath() => _steamCmdPrefixPath;

        public async Task SetSteamCmdPrefixPathAsync(string prefixPath)
        {
            if (string.IsNullOrWhiteSpace(prefixPath)) return;
            _steamCmdPrefixPath = prefixPath;
            _configService.SetConfigValue(SteamCmdPrefixConfigKey, prefixPath);
            await Task.Run(() => _configService.SaveConfig()); // Make config saving asynchronous
            InitializePaths(); // Re-calculate all derived paths
        }

    }
}