using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Service definition for retrieving mod compatibility information
    /// derived from community "No Version Warning" sources.
    /// </summary>
    public interface IMlieVersionService
    {
        /// <summary>
        /// Compatibility versions keyed by lower-cased package id. Each entry
        /// carries the display name of the external source that contributed it.
        /// When several enabled sources declare the same package id + version,
        /// the first source in registry order wins the attribution.
        /// </summary>
        Dictionary<string, List<ExternalVersionInfo>> GetExternalVersions();

        /// <summary>
        /// Flat projection of <see cref="GetExternalVersions"/> (version strings
        /// only), kept for consumers that do not need source attribution.
        /// </summary>
        Dictionary<string, List<string>> GetMlieVersions();

        /// <summary>
        /// Clears the in-memory cache so subsequent lookups re-load from the configured sources.
        /// </summary>
        void Invalidate();
    }
}
