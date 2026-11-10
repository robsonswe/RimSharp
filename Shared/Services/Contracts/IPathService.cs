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
        /// Resolves the local mods folder override slot for the *specified* game folder.
        /// A valid mods_folder override wins; otherwise the raw derivation applies
        /// (Path.Combine(gamePath, "Mods")). No existence check on the derived result
        /// and no cache — callers that need those use GetModsPath(). This is the
        /// single writer rule for GameInstance.LocalFolder:
        /// every writer of that property must go through this method or GetModsPath(),
        /// never a direct Path.Combine.
        /// </summary>
        /// <param name="gamePath">The game installation folder to derive from.</param>
        /// <returns>The resolved mods folder path (may not exist on disk).</returns>
        string GetModsPathFor(string gamePath);

        /// <summary>
        /// Resolves the game Data folder path for core/DLC mods, bundle-aware on macOS.
        /// Candidate order: <game>/Contents/Data, <game>/Contents/Resources/Data, <game>/Data.
        /// Returns the FIRST that exists. If none exist, returns the legacy non-checking
        /// fallback <game>/Data (preserving current behavior for missing Data folders).
        /// Empty input returns empty string.
        /// </summary>
        /// <param name="gamePath">The game installation folder to derive from.</param>
        /// <returns>The resolved Data folder path (may not exist on disk).</returns>
        string GetGameDataPathFor(string gamePath);


        /// <summary>

        /// which contains saves, settings, and ModsConfig.xml.
        /// </summary>

        string GetConfigPath();

        /// <summary>
        /// Resolves the saves folder of the game: the "Saves" SIBLING folder of
        /// the config folder (Path.Combine(Directory.GetParent(configPath), "Saves")).
        /// Single writer rule: every reader of the saves folder goes through this
        /// method, never through a direct Path.Combine.
        /// Note: an unresolved config path yields string.Empty -
        /// callers check Directory.Exists before using the result.
        /// </summary>
        string GetSavesPath();

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
