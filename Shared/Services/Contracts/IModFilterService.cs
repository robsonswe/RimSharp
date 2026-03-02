using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModFilterService
    {
        event System.EventHandler? FilteringCompleted;

        // Keep existing properties for direct search text access
        string ActiveSearchText { get; }
        string InactiveSearchText { get; }

        ModFilterCriteria ActiveFilterCriteria { get; }
        ModFilterCriteria InactiveFilterCriteria { get; }

        // Expose available options for the dialog
        IEnumerable<string> AllAvailableSupportedVersions { get; }
        IEnumerable<string> AllAvailableTags { get; }
        IEnumerable<string> AllAvailableAuthors { get; }

        ObservableCollection<ModItem> ActiveMods { get; }
        ObservableCollection<ModItem> InactiveMods { get; }

        // Keep existing methods for simple search text filtering
        void ApplyActiveFilter(string searchText);
        void ApplyInactiveFilter(string searchText);

        void ApplyActiveFilterCriteria(ModFilterCriteria criteria);
        void ApplyInactiveFilterCriteria(ModFilterCriteria criteria);

        void UpdateCollections(IEnumerable<(ModItem Mod, int LoadOrder)> activeMods, IEnumerable<ModItem> inactiveMods);

        // Method to clear filters
        void ClearActiveFilters();
        void ClearInactiveFilters();
    }
}


