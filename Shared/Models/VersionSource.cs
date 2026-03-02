namespace RimSharp.Shared.Models
{
    /// <summary>

    /// </summary>
    public enum VersionSource
    {
        /// <summary>
        /// Version listed in the mod's About.xml.
        /// </summary>
        Official,
        /// <summary>

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

