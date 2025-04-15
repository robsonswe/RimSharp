using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Service definition for retrieving mod replacement information from various sources.
    /// </summary>
    public interface IModReplacementService
    {
        /// <summary>
        /// Gets a dictionary of all known mod replacements, keyed by the original mod's Steam ID (lowercase).
        /// The data is loaded and cached on the first call, prioritizing the database source.
        /// </summary>
        /// <returns>
        /// A dictionary where the key is the lowercase original Steam Workshop ID and the value
        /// is the <see cref="ModReplacementInfo"/>. Returns an empty dictionary if no replacements are found.
        /// </returns>
        Dictionary<string, ModReplacementInfo> GetAllReplacements();

        /// <summary>
        /// Gets replacement information for a specific mod based on its original Steam Workshop ID.
        /// </summary>
        /// <param name="steamId">The original Steam Workshop ID of the mod to look up.</param>
        /// <returns>The <see cref="ModReplacementInfo"/> if found, otherwise null.</returns>
        ModReplacementInfo GetReplacementBySteamId(string steamId);

        /// <summary>
        /// Gets replacement information for a specific mod based on its original package ID (ModId).
        /// Note: This lookup might be less efficient than by Steam ID if multiple mods share a package ID (though unlikely).
        /// </summary>
        /// <param name="packageId">The original package ID (ModId) of the mod to look up.</param>
        /// <returns>The <see cref="ModReplacementInfo"/> if found, otherwise null.</returns>
        ModReplacementInfo GetReplacementByPackageId(string packageId);
    }
}
