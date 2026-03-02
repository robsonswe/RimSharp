#nullable enable
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// </summary>
    public interface IModService
    {
        /// <summary>

/// </summary>

        IEnumerable<ModItem> GetLoadedMods();

        /// <summary>

/// and determines the active status based on ModsConfig.xml.
        /// This operation blocks the calling thread until completion.
        /// </summary>
        void LoadMods();

        /// <summary>

/// and determines the active status based on ModsConfig.xml.

        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous loading operation.</returns>
        Task LoadModsAsync(IProgress<(int current, int total, string message)>? progress = null);

        /// <summary>

        /// within the specified directory.
        /// </summary>

Task CreateTimestampFilesAsync(string modDirectoryPath, string steamId, string publishDate, string standardDate);

        /// <summary>
        /// Retrieves the custom information for a specific mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>

        ModCustomInfo? GetCustomModInfo(string packageId);

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


