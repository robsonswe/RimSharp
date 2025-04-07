namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines the contract for a service that manages application configuration settings.
    /// </summary>
    public interface IConfigService
    {
        /// <summary>
        /// Loads or reloads configuration settings from the persistent storage (e.g., a config file).
        /// This typically replaces any currently held configuration values in memory.
        /// May create a default configuration if none exists.
        /// </summary>
        void LoadConfig();

        /// <summary>
        /// Saves the current configuration settings held in memory to the persistent storage (e.g., a config file).
        /// </summary>
        void SaveConfig();

        /// <summary>
        /// Retrieves the configuration value associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the configuration value to retrieve.</param>
        /// <returns>The configuration value string, or an empty string if the key is not found.</returns>
        string GetConfigValue(string key);

        /// <summary>
        /// Sets or updates the configuration value for the specified key in memory.
        /// Note: Changes are not persisted until <see cref="SaveConfig"/> is called.
        /// </summary>
        /// <param name="key">The key of the configuration value to set.</param>
        /// <param name="value">The value to associate with the key.</param>
        void SetConfigValue(string key, string value);
    }
}