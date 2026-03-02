namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// and methods for retrieving game version information.
    /// </summary>
    public interface IPathService
    {
        /// <summary>

        /// </summary>

        string GetGamePath();

        /// <summary>

        /// </summary>

        string GetModsPath();

        /// <summary>

        /// which contains saves, settings, and ModsConfig.xml.
        /// </summary>

        string GetConfigPath();

        /// <summary>

/// </summary>

        string GetGameVersion();

        /// <summary>

        /// from the *specified* game path.
        /// </summary>
        /// <param name="gamePath">The specific game installation path to check.</param>

        string GetGameVersion(string gamePath);

        /// <summary>

/// </summary>

        string GetMajorGameVersion();

        /// <summary>

        /// based on the *specified* game path.
        /// </summary>
        /// <param name="gamePath">The specific game installation path to check.</param>

        string GetMajorGameVersion(string gamePath);
        void RefreshPaths();
    }
}


