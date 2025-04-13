using RimSharp.Shared.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// Defines the contract for a service that manages custom user-defined
    /// complementary information for mods.
    /// </summary>
    public interface IModCustomService
    {
        /// <summary>
        /// Retrieves all custom mod data.
        /// </summary>
        /// <returns>A dictionary mapping package IDs to custom mod information.</returns>
        Dictionary<string, ModCustomInfo> GetAllCustomMods();

        /// <summary>
        /// Gets custom information for a specific mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>
        /// <returns>The custom information for the mod, or null if not found.</returns>
        ModCustomInfo GetCustomModInfo(string packageId);

        /// <summary>
        /// Adds or updates custom information for a mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>
        /// <param name="customInfo">The custom information to save.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SaveCustomModInfoAsync(string packageId, ModCustomInfo customInfo);

        /// <summary>
        /// Removes custom information for a mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task RemoveCustomModInfoAsync(string packageId);

        /// <summary>
        /// Applies the custom mod information to the standard mod items.
        /// </summary>
        /// <param name="mods">The collection of mods to apply custom information to.</param>
        void ApplyCustomInfoToMods(IEnumerable<ModItem> mods);
    }
}