using System.Collections.Generic;
using RimSharp.Shared.Models;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Applies locally-recorded ACF timestamps (from appworkshop_294100.acf) onto
    /// scanned mods, keyed by ModItem.SteamId. Keeps scans cheap via a JSON cache
    /// invalidated by the ACF file's LastWriteTimeUtc.
    /// </summary>
    public interface IWorkshopAcfTimestampService
    {
        /// <summary>
        /// ACF-derived timestamps (ResolvedTimeUpdated, unix seconds) keyed by
        /// publishedfileid. Empty when there is no workshop manifest to read
        /// (non-Steam setup, missing file, or unusable manifest).
        /// </summary>
        IReadOnlyDictionary<string, long> GetTimestamps(string? workshopFolder);

        /// <summary>
        /// Sets ModItem.AcfTimeUpdated for every mod whose SteamId matches an ACF
        /// record — explicitly clearing it (null) for Steam-carrying mods that have
        /// no record. Mods without a SteamId are never touched.
        /// </summary>
        void ApplyToMods(IEnumerable<ModItem> mods, string? workshopFolder);
    }
}
