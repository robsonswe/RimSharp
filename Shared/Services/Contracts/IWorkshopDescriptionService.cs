#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// File name of the per-mod Workshop metadata cache (inside the mod folder).
    /// </summary>
    public static class WorkshopDescriptionCache
    {
        public const string FileName = "RimSharp.json";
    }

    /// <summary>
    /// Fetches the full Workshop description for a mod's SteamId and caches it
    /// next to About.xml so the rich text survives locally. Two extractors:
    /// the Steam Web API first, an HTML page scrape as fallback.
    /// </summary>
    public interface IWorkshopDescriptionService
    {
        /// <summary>
        /// Fetches and caches the Workshop description for the given SteamId.
        /// Returns the plain-text description, or null when unavailable.
        /// </summary>
        Task<string?> EnrichModAsync(string steamId, string modFolderPath, CancellationToken ct = default);

        /// <summary>
        /// Persists an ALREADY-EXTRACTED plain-text description (e.g. captured
        /// from the embedded browser, or carried through the download queue)
        /// into the mod folder's RimSharp.json. Returns true when written.
        /// </summary>
        Task<bool> SaveCacheAsync(string steamId, string modFolderPath, string plainText, string source, string? steamName = null, CancellationToken ct = default);
    }
}
