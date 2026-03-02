using RimSharp.Shared.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>

    /// complementary information for mods.
    /// </summary>
    public interface IModCustomService
    {
        /// <summary>

        /// Call this during app startup.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Retrieves all custom mod data.
        /// </summary>

        Dictionary<string, ModCustomInfo> GetAllCustomMods();

        /// <summary>
        /// Gets custom information for a specific mod.
        /// </summary>
        /// <param name="packageId">The package ID of the mod.</param>

        ModCustomInfo? GetCustomModInfo(string packageId);

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

        /// </summary>

        void ApplyCustomInfoToMods(IEnumerable<ModItem> mods);
    }
}
