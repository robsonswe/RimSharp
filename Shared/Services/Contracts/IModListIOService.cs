using System.Collections.Generic;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModListIOService
    {
        /// <summary>
        /// Imports a mod list the user picks: a file dialog opens in the Lists
        /// folder, the picked file is parsed and its list is applied.
        /// </summary>
        Task ImportModListAsync();

        /// <summary>
        /// Imports a mod list that is already known - the same pipeline as
        /// <see cref="ImportModListAsync()"/> without the file picker: the mods
        /// that are not installed are computed, the list is applied to the
        /// active mods and the same result dialogs are shown (including the
        /// offer to download the missing mods). Nothing is dropped silently.
        /// </summary>
        /// <param name="orderedModIds">The package ids, in the load order the list was recorded in.</param>
        /// <param name="sourceLabel">What the list came from - named in the result dialogs.</param>
        Task ImportModListAsync(IReadOnlyList<string> orderedModIds, string sourceLabel);

        Task ExportModListAsync(IEnumerable<ModItem> activeMods);
    }
}
