namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines the contract for a service that provides access to essential RimWorld-related file system paths
    /// and methods for retrieving game version information.
    /// </summary>
    public interface IPathService
    {
        /// <summary>
        /// Gets the validated path to the main RimWorld game installation directory.
        /// </summary>
        /// <returns>The full path to the game directory if configured and valid; otherwise, potentially null or empty.</returns>
        string GetGamePath();

        /// <summary>
        /// Gets the validated path to the directory containing RimWorld mods (e.g., Steam Workshop content).
        /// </summary>
        /// <returns>The full path to the mods directory if configured and valid; otherwise, potentially null or empty.</returns>
        string GetModsPath();

        /// <summary>
        /// Gets the validated path to the RimWorld configuration directory (typically in user app data),
        /// which contains saves, settings, and ModsConfig.xml.
        /// </summary>
        /// <returns>The full path to the config directory if configured and valid; otherwise, potentially null or empty.</returns>
        string GetConfigPath();

        /// <summary>
        /// Gets the full game version string (e.g., "1.5.4409 rev1118") by reading Version.txt
        /// from the game path currently configured *within the service*.
        /// </summary>
        /// <returns>The full version string, or an "N/A..." message if the path is invalid, the file is missing, or an error occurs.</returns>
        string GetGameVersion();

        /// <summary>
        /// Gets the full game version string (e.g., "1.5.4409 rev1118") by reading Version.txt
        /// from the *specified* game path.
        /// </summary>
        /// <param name="gamePath">The specific game installation path to check.</param>
        /// <returns>The full version string, or an "N/A..." message if the path is invalid, the file is missing, or an error occurs.</returns>
        string GetGameVersion(string gamePath);

        /// <summary>
        /// Gets the major game version (e.g., "1.5") derived from the full version string,
        /// based on the game path currently configured *within the service*.
        /// </summary>
        /// <returns>The major version string (e.g., "1.5"), or the original full version/error message if parsing fails.</returns>
        string GetMajorGameVersion();

        /// <summary>
        /// Gets the major game version (e.g., "1.5") derived from the full version string,
        /// based on the *specified* game path.
        /// </summary>
        /// <param name="gamePath">The specific game installation path to check.</param>
        /// <returns>The major version string (e.g., "1.5"), or the original full version/error message if parsing fails.</returns>
        string GetMajorGameVersion(string gamePath);
        void RefreshPaths();
    }
}