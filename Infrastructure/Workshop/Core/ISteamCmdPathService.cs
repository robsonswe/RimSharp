using System.Threading.Tasks;

namespace RimSharp.Infrastructure.Workshop.Core
{
    public interface ISteamCmdPathService
    {
        string SteamCmdPrefixPath { get; }
        string SteamCmdInstallPath { get; }
        string? SteamCmdExePath { get; }
        string SteamCmdWorkshopContentPath { get; }
        string SteamCmdDepotCachePath { get; }
        string SteamCmdSteamAppsPath { get; }

        /// <summary>
        /// Directory where SteamCMD writes its log files (workshop_log.txt,
        /// content_log.txt, bootstrap_log.txt). Platform-aware: on Windows the
        /// Steam dir is the steamcmd executable's own directory, while on
        /// Linux/macOS SteamCMD resolves it from $HOME (redirected to an
        /// app-managed "steam-home" directory by the process runner).
        /// </summary>
        string SteamCmdLogDirectory { get; }

        Task SetSteamCmdPrefixPathAsync(string prefixPath);
        string GetSteamCmdPrefixPath();
        void InitializePaths();
    }
}