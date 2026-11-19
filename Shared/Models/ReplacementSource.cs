namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Indicates the origin of the mod replacement information.
    /// </summary>
    public enum ReplacementSource
    {
        /// <summary>
        /// Information loaded from the replacements.json database file.
        /// </summary>
        Database,

        /// <summary>
        /// Information loaded from the locally installed 'Use This Instead' mod.
        /// </summary>
        UseThisInstead,

        /// <summary>
        /// Information loaded from the emipa606/UseThisInstead GitHub repository
        /// snapshot kept in the app data directory.
        /// </summary>
        UseThisInsteadRepo,

        /// <summary>
        /// Information loaded from a user-configured local replacement file.
        /// The human-readable registry source name is carried on
        /// <see cref="ModReplacementInfo.SourceName"/> because it is user-defined.
        /// </summary>
        LocalFile,

        /// <summary>
        /// Information loaded from an index-driven external replacement source
        /// (see <see cref="ModRuleSourceType.IndexedRecords"/>). The registry
        /// source's display name is carried on
        /// <see cref="ModReplacementInfo.SourceName"/>.
        /// </summary>
        IndexedRecords
    }
}

