namespace RimSharp.Services
{
    public interface IPathService
    {
        string GetGamePath();
        string GetModsPath();
        string GetConfigPath();
        string GetGameVersion(); // Gets version based on internally configured path
        string GetGameVersion(string gamePath); // Gets version based on a specific path
        string GetMajorGameVersion(); // Gets just the first two numbers (e.g. "1.5")
        string GetMajorGameVersion(string gamePath); // Gets just the first two numbers from specific path

    }
}