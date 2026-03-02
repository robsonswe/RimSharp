using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSharp.Features.WorkshopDownloader.Models
{

    public class SteamApiResponse
    {
        [JsonPropertyName("response")]
        public SteamApiResponseDetails Response { get; set; } = new();
    }

    public class SteamApiResponseDetails
    {
        [JsonPropertyName("result")]
        public int Result { get; set; } // 1 for success

        [JsonPropertyName("resultcount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("publishedfiledetails")]
        public List<SteamPublishedFileDetails> PublishedFileDetails { get; set; } = new();
    }

    public class SteamPublishedFileDetails
    {
        [JsonPropertyName("publishedfileid")]
        public string PublishedFileId { get; set; } = string.Empty; // String, though usually numeric

        [JsonPropertyName("result")]
        public int Result { get; set; } // 1 for success for this specific item

        [JsonPropertyName("consumer_app_id")]
        public int ConsumerAppId { get; set; } // Should be 294100 for RimWorld

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("time_updated")]
        public long TimeUpdated { get; set; } // Unix timestamp (seconds since epoch)

        [JsonPropertyName("file_size")]
        public long FileSize { get; set; }

        [JsonPropertyName("tags")]
        public List<SteamTag> Tags { get; set; } = new();

        // [JsonPropertyName("description")]
        // public string Description { get; set; }
    }

    public class SteamTag
    {
        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;
    }
}


