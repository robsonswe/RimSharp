#nullable enable
using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Automatic Workshop tags captured from the Steam API / HTML page.
        /// Only "Translation" and "Scenario" are retained; version-like tags
        /// (e.g. "1.6") are discarded as noise. Merged with user-defined tags
        /// (case-insensitively) at load time; read-only from the user's view.
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>"steamapi" or "html" — which extractor produced the text.</summary>
        public string Source { get; set; } = string.Empty;

        public long TimeUpdated { get; set; }

        public DateTimeOffset FetchedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
