using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSharp.Features.WorkshopDownloader.Models
{
    // Represents the overall JSON structure from the Steam API
    public class SteamApiResponse
    {
        [JsonPropertyName("response")]
        public SteamApiResponseDetails Response { get; set; }
    }

    public class SteamApiResponseDetails
    {
        [JsonPropertyName("result")]
        public int Result { get; set; } // 1 for success

        [JsonPropertyName("resultcount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("publishedfiledetails")]
        public List<SteamPublishedFileDetails> PublishedFileDetails { get; set; }
    }

    public class SteamPublishedFileDetails
    {
        [JsonPropertyName("publishedfileid")]
        public string PublishedFileId { get; set; } // String, though usually numeric

        [JsonPropertyName("result")]
        public int Result { get; set; } // 1 for success for this specific item

        [JsonPropertyName("consumer_app_id")]
        public int ConsumerAppId { get; set; } // Should be 294100 for RimWorld

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("time_updated")]
        public long TimeUpdated { get; set; } // Unix timestamp (seconds since epoch)

        [JsonPropertyName("file_size")] // Added
        public long FileSize { get; set; } // Added: Size in bytes

        // Add other fields if needed later, like description, preview_url etc.
        // [JsonPropertyName("description")]
        // public string Description { get; set; }
    }
}
