namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Where a mod's supported-version entry came from. There are exactly three
    /// sources: the mod's own metadata, a community external source, or the user.
    /// </summary>
    public enum VersionSource
    {
        /// <summary>
        /// Version listed in the mod's About.xml.
        /// </summary>
        Official,

        /// <summary>
        /// Version contributed by a community external source (e.g. a
        /// "No Version Warning" source). The contributing source's display name
        /// is carried on the <see cref="VersionSupport"/> entry itself.
        /// </summary>
        External,

        /// <summary>
        /// Version added via user's custom configuration.
        /// </summary>
        Custom
    }
}
