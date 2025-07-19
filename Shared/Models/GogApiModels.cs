#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Represents the top-level object returned by the GOG content system API.
    /// </summary>
    public class GogApiResponse
    {
        [JsonPropertyName("items")]
        public List<GogBuildInfo>? Items { get; set; }
    }

    /// <summary>
    /// Represents a single build's information from the GOG.com content system API.
    /// </summary>
    public class GogBuildInfo
    {
        [JsonPropertyName("version_name")]
        public string? VersionName { get; set; }

        [JsonPropertyName("public")]
        public bool IsPublic { get; set; }
    }
}