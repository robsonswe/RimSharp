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
        /// Gets a dictionary mapping mod package IDs (lowercase) to a list of
        /// RimWorld version strings they are marked compatible with by Mlie's mod.
        /// The data is loaded and cached on the first call.
        /// </summary>
        /// <returns>
        /// A dictionary where the key is the lowercase mod package ID and the value
        /// is a list of version strings (e.g., "1.3", "1.4"). Returns an empty
        /// dictionary if the Mlie mod is not found or data cannot be parsed.
        /// </returns>
        Dictionary<string, List<string>> GetMlieVersions();
    }
}
