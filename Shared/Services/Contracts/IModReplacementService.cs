using RimSharp.Shared.Models;
using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IModReplacementService
    {
        /// <summary>

/// </summary>
        /// <returns>

/// </returns>
        Dictionary<string, ModReplacementInfo> GetAllReplacements();

        /// <summary>

        /// </summary>

        /// <returns>The <see cref="ModReplacementInfo"/> if found, otherwise null.</returns>
        ModReplacementInfo? GetReplacementBySteamId(string steamId);

        /// <summary>

/// </summary>

        /// <returns>The <see cref="ModReplacementInfo"/> if found, otherwise null.</returns>
        ModReplacementInfo? GetReplacementByPackageId(string packageId);
    }
}

