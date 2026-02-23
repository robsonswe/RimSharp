// Add this using statement if not already present
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Represents information about a mod replacement suggestion.
    /// </summary>
    public class ModReplacementInfo
    {
        // Original Mod Info
        public string Author { get; set; } = string.Empty;
        public string ModId { get; set; } = string.Empty; // Original PackageId
        public string ModName { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty; // Primary Key (Original Steam ID)
        public string Versions { get; set; } = string.Empty; // Comma-separated string

        // Replacement Mod Info
        public string ReplacementAuthor { get; set; } = string.Empty;
        public string ReplacementModId { get; set; } = string.Empty; // Replacement PackageId
        public string ReplacementName { get; set; } = string.Empty;
        public string ReplacementSteamId { get; set; } = string.Empty;
        public string ReplacementVersions { get; set; } = string.Empty; // Comma-separated string

        // Source of this information
        public ReplacementSource Source { get; set; }

        [JsonIgnore]
        public string? ReplacementSteamUrl => !string.IsNullOrEmpty(ReplacementSteamId)
                                            ? $"https://steamcommunity.com/sharedfiles/filedetails/?id={ReplacementSteamId}"
                                            : null;

        // Calculated properties now correctly return simple string lists
        [JsonIgnore]
        public List<string> OriginalVersionList => ParseVersionString(Versions);
        [JsonIgnore]
        public List<string> ReplacementVersionList => ParseVersionString(ReplacementVersions);

        private static List<string> ParseVersionString(string versions)
        {
            if (string.IsNullOrWhiteSpace(versions))
            {
                return new List<string>();
            }
            return versions.Split(',')
                           .Select(v => v.Trim())
                           .Where(v => !string.IsNullOrEmpty(v))
                           .ToList();
        }
    }

    /// <summary>
    /// Structure matching the replacements.json file format.
    /// </summary>
        internal class ReplacementJsonRoot // Internal as it's only used for deserialization
        {
            public Dictionary<string, ModReplacementInfo> Mods { get; set; } = new();
        }
    }
    