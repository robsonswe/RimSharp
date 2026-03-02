namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IConfigService
    {
        /// <summary>

/// May create a default configuration if none exists.
        /// </summary>
        void LoadConfig();

        /// <summary>

        /// </summary>
        void SaveConfig();

        /// <summary>

        /// </summary>

string GetConfigValue(string key);

        /// <summary>

/// </summary>

        /// <param name="value">The value to associate with the key.</param>
        void SetConfigValue(string key, string value);
    }
}
