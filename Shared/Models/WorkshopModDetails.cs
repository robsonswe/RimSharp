#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Unified Steam Workshop item detail record — the single canonical shape
    /// used by the central client and all consumers (downloader adapter, etc.).
    /// Replaces the duplicate `SteamPublishedFileDetails` in the downloader models.
    /// </summary>
    public class WorkshopModDetails
    {
        [JsonPropertyName("publishedfileid")]
        public string? PublishedFileId { get; set; }

        [JsonPropertyName("result")]
        public int Result { get; set; }

        [JsonPropertyName("consumer_appid")]
        public long? ConsumerAppId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("time_created")]
        public long? TimeCreated { get; set; }

        [JsonPropertyName("time_updated")]
        public long? TimeUpdated { get; set; }

        [JsonPropertyName("file_size")]
        public long? FileSize { get; set; }

        [JsonPropertyName("preview_url")]
        public string? PreviewUrl { get; set; }

        [JsonPropertyName("tags")]
        public List<WorkshopTag>? Tags { get; set; }

        [JsonPropertyName("children")]
        public List<WorkshopChild>? Children { get; set; }
    }

    /// <summary>
    /// Workshop tag — used by `WorkshopModDetails.Tags` and by the downloader's
    /// `SteamApiResultHelper.ExtractAndSortVersionTags`.
    /// </summary>
    public class WorkshopTag
    {
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }
    }

    /// <summary>
    /// Workshop dependency / child item reference.
    /// </summary>
    public class WorkshopChild
    {
        [JsonPropertyName("publishedfileid")]
        public string? PublishedFileId { get; set; }
    }
}