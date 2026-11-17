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
        UseThisInsteadRepo
    }
}

