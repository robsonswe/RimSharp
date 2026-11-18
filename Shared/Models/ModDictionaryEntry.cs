using System.Collections.Generic;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Normalized view of a mod dictionary entry coming from any ModInfo source.
    /// </summary>
    public class ModDictionaryEntry
    {
        /// <summary>
        /// The packageId (folder name) of the mod. Lowercase.
        /// </summary>
        public string PackageId { get; set; } = string.Empty;

        /// <summary>
        /// The Steam Workshop ID of the mod. Lowercase.
        /// </summary>
        public string SteamId { get; set; } = string.Empty;

        private string _legacyName = string.Empty;

        /// <summary>
        /// Display name: the workshop title first, then the About.xml name.
        /// The setter is retained for backwards compatibility with callers that
        /// construct entries with a plain <c>Name</c>; it is only used as a
        /// last-resort fallback when neither <see cref="SteamName"/> nor
        /// <see cref="AboutName"/> is available.
        /// </summary>
        public string Name
        {
            get => !string.IsNullOrWhiteSpace(SteamName)
                ? SteamName
                : !string.IsNullOrWhiteSpace(AboutName)
                    ? AboutName
                    : _legacyName;
            set => _legacyName = value;
        }

        /// <summary>
        /// Workshop title (the preferred display name).
        /// </summary>
        public string SteamName { get; set; } = string.Empty;

        /// <summary>
        /// Name extracted from About.xml (fallback display name).
        /// </summary>
        public string AboutName { get; set; } = string.Empty;

        /// <summary>
        /// Id of the ModInfo source that first contributed this entry.
        /// </summary>
        public string SourceId { get; set; } = string.Empty;

        /// <summary>
        /// List of supported game versions.
        /// </summary>
        public List<string> Versions { get; set; } = new List<string>();

        /// <summary>
        /// The author(s) of the mod.
        /// </summary>
        public string Authors { get; set; } = string.Empty;

        /// <summary>
        /// Whether the workshop item is still published.
        /// </summary>
        public bool Published { get; set; }
    }
}
