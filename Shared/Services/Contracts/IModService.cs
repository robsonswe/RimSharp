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
    }
}