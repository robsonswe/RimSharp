using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Service definition for retrieving mod information from the central dictionary database (db.json).
    /// </summary>
    public interface IModDictionaryService
    {
        /// <summary>
        /// Gets a dictionary of all known mod entries from the database, keyed by the mod's Steam ID (lowercase).
        /// The data is loaded and cached on the first call.
        /// </summary>
        /// <returns>
        /// A dictionary where the key is the lowercase Steam Workshop ID and the value
        /// is the <see cref="ModDictionaryEntry"/>. Returns an empty dictionary if the database is empty or cannot be loaded.
        /// </returns>
        Dictionary<string, ModDictionaryEntry> GetAllEntries();

        /// <summary>
        /// Gets dictionary entry information for a specific mod based on its Steam Workshop ID.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID of the mod to look up.</param>
        /// <returns>The <see cref="ModDictionaryEntry"/> if found, otherwise null.</returns>
        ModDictionaryEntry GetEntryBySteamId(string steamId);

        /// <summary>
        /// Gets dictionary entry information for a specific mod based on its package ID (ModId).
        /// Note: If multiple Steam IDs exist for the same package ID in the database, this method
        /// currently returns the *first* one found during the iteration.
        /// </summary>
        /// <param name="packageId">The package ID (ModId) of the mod to look up.</param>
        /// <returns>The first matching <see cref="ModDictionaryEntry"/> if found, otherwise null.</returns>
        ModDictionaryEntry GetEntryByPackageId(string packageId);

        /// <summary>
        /// Gets *all* dictionary entry information for a specific mod based on its package ID (ModId).
        /// Useful when a mod might have multiple entries (e.g., different Steam IDs for different versions).
        /// </summary>
        /// <param name="packageId">The package ID (ModId) of the mod to look up.</param>
        /// <returns>A list of all matching <see cref="ModDictionaryEntry"/> objects. Returns an empty list if none are found.</returns>
        List<ModDictionaryEntry> GetAllEntriesByPackageId(string packageId);
    }
}