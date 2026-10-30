#nullable enable
using System;

namespace RimSharp.Shared.Models
{
    /// <summary>
    /// Cached Workshop metadata for a single installed mod, persisted as
    /// "RimSharp.json" inside the mod folder (next to About.xml).
    /// </summary>
    public class WorkshopDescriptionRecord
    {
        public string SteamId { get; set; } = string.Empty;

        /// <summary>Plain-text, tag-stripped Workshop description.</summary>
        public string SteamDescription { get; set; } = string.Empty;

        /// <summary>Workshop item title (from Steam API / HTML page).</summary>
        public string SteamName { get; set; } = string.Empty;

        /// <summary>"steamapi" or "html" — which extractor produced the text.</summary>
        public string Source { get; set; } = string.Empty;

        public long TimeUpdated { get; set; }

        public DateTimeOffset FetchedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
