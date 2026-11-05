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
        /// Derives the local mods folder for the *specified* game folder:
        /// Path.Combine(gamePath, "Mods"). No existence check, no cache — the raw
        /// derivation. This is the single writer rule for GameInstance.LocalFolder
        /// (handoff doc §6 Phase 0): every writer of that property must go through
        /// this method or GetModsPath(), never a direct Path.Combine.
        /// </summary>
        /// <param name="gamePath">The game installation folder to derive from.</param>
        /// <returns>The derived mods folder path (may not exist on disk).</returns>
        string GetModsPathFor(string gamePath);


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


