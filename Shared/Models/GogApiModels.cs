#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSharp.Shared.Models
{
    /// <summary>

    /// </summary>
    public class GogApiResponse
    {
        [JsonPropertyName("items")]
        public List<GogBuildInfo>? Items { get; set; }
    }

    /// <summary>

    /// </summary>
    public class GogBuildInfo
    {
        [JsonPropertyName("version_name")]
        public string? VersionName { get; set; }

        [JsonPropertyName("public")]
        public bool IsPublic { get; set; }
    }
}
