using System.Collections.Generic;
using System.Text.Json.Serialization;
using RimSharp.Shared.JsonConverters;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Represents user-defined custom information for a mod.
    /// </summary>
    public class ModCustomInfo
    {
        /// <summary>
        /// Custom load before rules defined by the user.
        /// </summary>
        public Dictionary<string, ModDependencyRule> LoadBefore { get; set; } = new();

        /// <summary>
        /// Custom load after rules defined by the user.
        /// </summary>
        public Dictionary<string, ModDependencyRule> LoadAfter { get; set; } = new();

        /// <summary>
        /// Custom load bottom rule defined by the user.
        /// </summary>
        public LoadBottomRule LoadBottom { get; set; }

        /// <summary>
        /// Additional game versions the mod supports according to user.
        /// </summary>
        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> SupportedVersions { get; set; } = new();

        /// <summary>
        /// Custom incompatibilities defined by the user.
        /// </summary>
        public Dictionary<string, ModIncompatibilityRule> IncompatibleWith { get; set; } = new();

        /// <summary>
        /// User-defined external URL for the mod.
        /// </summary>
        public string ExternalUrl { get; set; }

        /// <summary>
        /// User-defined tags for the mod.
        /// </summary>
        public string Tags { get; set; }

        /// <summary>
        /// User-defined favorite status for the mod.
        /// Null means not set, true means it's a favorite.
        /// </summary>
        public bool? Favorite { get; set; }
    }

    /// <summary>
    /// Root object for the custom mods JSON file.
    /// </summary>
    public class ModsCustomRoot
    {
        /// <summary>
        /// Dictionary of package IDs to custom mod information.
        /// </summary>
        public Dictionary<string, ModCustomInfo> Mods { get; set; } = new();
    }
}