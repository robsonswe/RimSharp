namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Indicates the origin of a supported version entry for a mod.
    /// </summary>
    public enum VersionSource
    {
        /// <summary>
        /// Version listed in the mod's About.xml.
        /// </summary>
        Official,
        /// <summary>
        /// Version derived from the Mlie's Forcing Mod XML files.
        /// </summary>
        Mlie,
        /// <summary>
        /// Version derived from the central Mod Rules database/JSON.
        /// </summary>
        Database,
        /// <summary>
        /// Version added via user's custom configuration.
        /// </summary>
        Custom
    }
}
