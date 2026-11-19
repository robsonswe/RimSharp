using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Provides replacement suggestions gathered from every enabled replacement
    /// source, in the user's persisted source-priority order.
    /// </summary>
    public interface IModReplacementService
    {
        /// <summary>
        /// All suggestions keyed by source Steam id (lower-cased) or
        /// <c>packageid_&lt;packageId&gt;</c>. Each value contains every distinct
        /// suggestion for that source mod, ordered by source priority.
        /// </summary>
        Dictionary<string, List<ModReplacementInfo>> GetAllReplacements();

        /// <summary>
        /// Returns the primary suggestion for a source Steam id (reason-carrying
        /// first, then source priority), or null when none exists.
        /// </summary>
        ModReplacementInfo? GetReplacementBySteamId(string steamId);

        /// <summary>
        /// Returns the primary suggestion for a source package id (reason-carrying
        /// first, then source priority), or null when none exists.
        /// </summary>
        ModReplacementInfo? GetReplacementByPackageId(string packageId);

        /// <summary>
        /// Returns every suggestion for a source Steam id, ordered by source priority.
        /// </summary>
        IReadOnlyList<ModReplacementInfo> GetReplacementsBySteamId(string steamId);

        /// <summary>
        /// Returns every suggestion for a source package id, ordered by source priority.
        /// </summary>
        IReadOnlyList<ModReplacementInfo> GetReplacementsByPackageId(string packageId);

        /// <summary>
        /// Clears the in-memory cache so subsequent lookups re-load from the configured sources.
        /// </summary>
        void Invalidate();
    }
}
