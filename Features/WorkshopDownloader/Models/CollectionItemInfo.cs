#nullable enable

namespace RimSharp.Features.WorkshopDownloader.Models
{
    /// <summary>
    /// Represents basic information extracted for an item within a Steam Workshop Collection page.
    /// </summary>
    public class CollectionItemInfo
    {
        public string SteamId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }
}
