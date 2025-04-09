using System.Threading.Tasks;

public interface ISteamCmdPathService
{
    string SteamCmdPrefixPath { get; }
    string SteamCmdInstallPath { get; }
    string SteamCmdExePath { get; }
    string SteamCmdWorkshopContentPath { get; }
    string SteamCmdDepotCachePath { get; }
    string SteamCmdSteamAppsPath { get; }
    
    Task SetSteamCmdPrefixPathAsync(string prefixPath);
    string GetSteamCmdPrefixPath();
    void InitializePaths();
}