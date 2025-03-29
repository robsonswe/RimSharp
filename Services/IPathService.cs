namespace RimSharp.Services
{
    public interface IPathService
    {
        string GetGamePath();
        string GetModsPath();
        string GetConfigPath();
        string GetGameVersion();
    }
}