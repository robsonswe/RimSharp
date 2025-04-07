using System.Collections.Generic;
using System.Collections.ObjectModel;
using RimSharp.Shared.Models;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModFilterService
    {
        string ActiveSearchText { get; }
        string InactiveSearchText { get; }

        // Interface exposes the standard ObservableCollection
        ObservableCollection<ModItem> ActiveMods { get; }
        ObservableCollection<ModItem> InactiveMods { get; }

        void ApplyActiveFilter(string searchText);
        void ApplyInactiveFilter(string searchText);
        // Method signature remains the same
        void UpdateCollections(IEnumerable<(ModItem Mod, int LoadOrder)> activeMods, IEnumerable<ModItem> inactiveMods);
    }
}
