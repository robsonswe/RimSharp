using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IModDictionaryService
    {
        /// <summary>

/// </summary>
        /// <returns>

/// </returns>
        Dictionary<string, ModDictionaryEntry> GetAllEntries();

        /// <summary>

        /// </summary>

        /// <returns>The <see cref="ModDictionaryEntry"/> if found, otherwise null.</returns>
        ModDictionaryEntry? GetEntryBySteamId(string steamId);

        /// <summary>

/// </summary>

ModDictionaryEntry? GetEntryByPackageId(string packageId);

        /// <summary>

/// </summary>

List<ModDictionaryEntry> GetAllEntriesByPackageId(string packageId);
    }
}
