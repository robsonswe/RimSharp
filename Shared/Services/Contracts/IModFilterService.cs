using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModFilterService
    {
        // Keep existing properties for direct search text access
        string ActiveSearchText { get; }
        string InactiveSearchText { get; }

        // Expose the current criteria objects (consider returning clones if mutation is a concern)
        ModFilterCriteria ActiveFilterCriteria { get; }
        ModFilterCriteria InactiveFilterCriteria { get; }

        // Expose available options for the dialog
        IEnumerable<string> AllAvailableSupportedVersions { get; }
        IEnumerable<string> AllAvailableTags { get; } // New
        IEnumerable<string> AllAvailableAuthors { get; } // New

        ObservableCollection<ModItem> ActiveMods { get; }
        ObservableCollection<ModItem> InactiveMods { get; }

        // Keep existing methods for simple search text filtering
        void ApplyActiveFilter(string searchText);
        void ApplyInactiveFilter(string searchText);

        // Add new methods for applying full criteria
        void ApplyActiveFilterCriteria(ModFilterCriteria criteria);
        void ApplyInactiveFilterCriteria(ModFilterCriteria criteria);

        // Method to update source data and extract filter options
        void UpdateCollections(IEnumerable<(ModItem Mod, int LoadOrder)> activeMods, IEnumerable<ModItem> inactiveMods);

        // Method to clear filters
        void ClearActiveFilters();
        void ClearInactiveFilters();
    }
}
