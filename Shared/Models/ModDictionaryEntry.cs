using System.Collections.Generic;
using System.Text.Json.Serialization; // If you need JSON attributes later

namespace RimSharp.Shared.Models // Adjust namespace as needed
{
    /// <summary>
    /// Represents a single mod entry loaded from the dictionary database (db.json).
    /// </summary>
    public class ModDictionaryEntry
    {
        /// <summary>
        /// The packageId (folder name) of the mod. Lowercase.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Steam Workshop ID of the mod. Lowercase.
        /// </summary>
        public string SteamId { get; set; }

        /// <summary>
        /// The display name of the mod.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// List of supported game versions.
        /// </summary>
        public List<string> Versions { get; set; } = new List<string>(); // Initialize to avoid nulls

        /// <summary>
        /// The author(s) of the mod.
        /// </summary>
        public string Authors { get; set; }

        // Add any calculated properties or methods if needed later
        // Example:
        // [JsonIgnore]
        // public string SteamUrl => !string.IsNullOrEmpty(SteamId)
        //                         ? $"https://steamcommunity.com/sharedfiles/filedetails/?id={SteamId}"
        //                         : null;
    }
}