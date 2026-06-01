using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSharp.Features.WorkshopDownloader.Models
{
    public class SteamCollectionDetailsResponse
    {
        [JsonPropertyName("response")]
        public SteamCollectionDetailsResponseDetails Response { get; set; } = new();
    }

    public class SteamCollectionDetailsResponseDetails
    {
        [JsonPropertyName("result")]
        public int Result { get; set; }

        [JsonPropertyName("collectiondetails")]
        public List<SteamCollectionDetails> CollectionDetails { get; set; } = new();
    }

    public class SteamCollectionDetails
    {
        [JsonPropertyName("publishedfileid")]
        public string PublishedFileId { get; set; } = string.Empty;

        [JsonPropertyName("children")]
        public List<SteamCollectionChild> Children { get; set; } = new();
    }

    public class SteamCollectionChild
    {
        [JsonPropertyName("publishedfileid")]
        public string PublishedFileId { get; set; } = string.Empty;

        [JsonPropertyName("sortorder")]
        public int SortOrder { get; set; }
    }
}
