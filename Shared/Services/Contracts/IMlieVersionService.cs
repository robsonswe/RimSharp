using System.Collections.Generic;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Service definition for retrieving mod compatibility information
    /// derived from the Mlie's Forcing Mod (ID: 2599504692).
    /// </summary>
    public interface IMlieVersionService
    {
        /// <summary>

/// </summary>
        /// <returns>

/// </returns>
        Dictionary<string, List<string>> GetMlieVersions();
    }
}

