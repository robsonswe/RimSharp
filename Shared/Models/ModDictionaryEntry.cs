using System.Collections.Generic;
using System.Text.Json.Serialization; // If you need JSON attributes later

namespace RimSharp.Shared.Models // Adjust namespace as needed
{
    /// <summary>

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

        /// <summary>
        /// The display name of the mod.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// List of supported game versions.
        /// </summary>
        public List<string> Versions { get; set; } = new List<string>(); // Initialize to avoid nulls

        /// <summary>
        /// The author(s) of the mod.
        /// </summary>
        public string Authors { get; set; } = string.Empty;

        /// <summary>

/// </summary>
        public bool Published { get; set; }

        // Example:
        // [JsonIgnore]
        // public string SteamUrl => !string.IsNullOrEmpty(SteamId)

}
}


