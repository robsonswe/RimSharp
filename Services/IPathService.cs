namespace RimSharp.Services
{
    public interface IPathService
    {
        string GetGamePath();
        string GetModsPath();
        string GetConfigPath();
        string GetGameVersion(); // Gets version based on internally configured path
        string GetGameVersion(string gamePath); // Gets version based on a specific path
    }
}