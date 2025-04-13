using RimSharp.Shared.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines the contract for a service that discovers, loads, and manages information about RimWorld mods.
    /// </summary>
    public interface IModService
    {
        /// <summary>
        /// Gets the collection of mod metadata that has been loaded by the service.
        /// The collection includes Core, DLC, and Workshop mods found in the configured paths.
        /// </summary>
        /// <returns>An enumerable collection of <see cref="ModItem"/> representing the loaded mods.</returns>
        IEnumerable<ModItem> GetLoadedMods();

        /// <summary>
        /// Synchronously discovers and loads metadata for all available RimWorld mods
        /// from the configured game data and mods directories. It parses About.xml files
        /// and determines the active status based on ModsConfig.xml.
        /// This operation blocks the calling thread until completion.
        /// </summary>
        void LoadMods();

        /// <summary>
        /// Asynchronously discovers and loads metadata for all available RimWorld mods
        /// from the configured game data and mods directories. It parses About.xml files
        /// and determines the active status based on ModsConfig.xml.
        /// This operation allows the calling thread to continue while loading occurs.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous loading operation.</returns>
        Task LoadModsAsync();
        
        /// <summary>
        /// Creates the DateStamp and timestamp.txt files for a successfully downloaded mod.
        /// </summary>
        /// <param name="steamId">The Steam Workshop ID of the mod.</param>
        /// <param name="publishDate">The publish date in Steam's format (d MMM yyyy @ h:mmtt).</param>
        /// <param name="standardDate">The publish date in standard format (dd/MM/yyyy HH:mm:ss).</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous file creation operation.</returns>
        Task CreateTimestampFilesAsync(string steamId, string publishDate, string standardDate);
        
        /// <summary>
        /// Retrieves the custom information for a specific mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>
        /// <returns>The custom information for the mod, or null if not found.</returns>
        ModCustomInfo GetCustomModInfo(string packageId);
        
        /// <summary>
        /// Saves custom information for a mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>
        /// <param name="customInfo">The custom information to save.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous save operation.</returns>
        Task SaveCustomModInfoAsync(string packageId, ModCustomInfo customInfo);
        
        /// <summary>
        /// Removes custom information for a mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous remove operation.</returns>
        Task RemoveCustomModInfoAsync(string packageId);
    }
}