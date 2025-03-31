namespace RimSharp.Services
{
    public interface IConfigService
    {
        void LoadConfig();
        void SaveConfig();
        string GetConfigValue(string key);
        void SetConfigValue(string key, string value);
    }
}