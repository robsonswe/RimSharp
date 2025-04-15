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
        /// Information loaded from the 'Use This Instead' mod XML files.
        /// </summary>
        UseThisInstead
    }
}
