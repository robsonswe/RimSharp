using RimSharp.Models;
using RimSharp.Utility; // Add using for BulkObservableCollection and DispatcherExtensions
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RimSharp.ViewModels.Modules.Mods.Filtering
{
    public class ModFilterService : IModFilterService
    {
        // --- Optimization: Use BulkObservableCollection ---
        public BulkObservableCollection<ModItem> ActiveMods { get; } = new BulkObservableCollection<ModItem>();
        public BulkObservableCollection<ModItem> InactiveMods { get; } = new BulkObservableCollection<ModItem>();
        // Expose as the specific type for AddRange access internally if needed,
        // but interface keeps ObservableCollection for compatibility.
        ObservableCollection<ModItem> IModFilterService.ActiveMods => ActiveMods;
        ObservableCollection<ModItem> IModFilterService.InactiveMods => InactiveMods;
        // ---------------------------------------------------


        private string _activeSearchText = string.Empty;
        private string _inactiveSearchText = string.Empty;
        public string ActiveSearchText => _activeSearchText;
        public string InactiveSearchText => _inactiveSearchText;

        // Store the full lists received from the manager
        private IEnumerable<(ModItem Mod, int LoadOrder)> _allActiveModsSource = Enumerable.Empty<(ModItem, int)>();
        private IEnumerable<ModItem> _allInactiveModsSource = Enumerable.Empty<ModItem>();


        public void ApplyActiveFilter(string searchText)
        {
            _activeSearchText = searchText ?? string.Empty;
            UpdateActiveModsCollection(); // Re-filter and update the observable collection
        }

        public void ApplyInactiveFilter(string searchText)
        {
            _inactiveSearchText = searchText ?? string.Empty;
            UpdateInactiveModsCollection(); // Re-filter and update the observable collection
        }

        // Called by ModsViewModel when the ModListManager signals changes
        public void UpdateCollections(IEnumerable<(ModItem Mod, int LoadOrder)> activeMods, IEnumerable<ModItem> inactiveMods)
        {
            _allActiveModsSource = activeMods ?? throw new ArgumentNullException(nameof(activeMods));
            _allInactiveModsSource = inactiveMods ?? throw new ArgumentNullException(nameof(inactiveMods));

            // Update both collections based on the new source data and current filters
            UpdateActiveModsCollection();
            UpdateInactiveModsCollection();
        }

        private void UpdateActiveModsCollection()
        {
            // Use SafeInvoke for thread safety when updating UI-bound collections
            Application.Current?.Dispatcher.SafeInvoke(UpdateActiveModsCollectionCore);
        }

        private void UpdateActiveModsCollectionCore()
        {
            // Filter the source list based on the current search text
            var filteredMods = string.IsNullOrWhiteSpace(_activeSearchText)
                ? _allActiveModsSource
                : _allActiveModsSource.Where(x =>
                    x.Mod.Name.Contains(_activeSearchText, StringComparison.OrdinalIgnoreCase) ||
                    x.Mod.PackageId.Contains(_activeSearchText, StringComparison.OrdinalIgnoreCase) // Also filter by ID
                    );

            // --- Optimization: Use ReplaceAll for bulk update ---
            // Sort by LoadOrder before adding to the collection
            ActiveMods.ReplaceAll(filteredMods.OrderBy(x => x.LoadOrder).Select(x => x.Mod));
            // ---------------------------------------------------
        }

        private void UpdateInactiveModsCollection()
        {
            // Use SafeInvoke for thread safety
            Application.Current?.Dispatcher.SafeInvoke(UpdateInactiveModsCollectionCore);
        }

        private void UpdateInactiveModsCollectionCore()
        {
            // Filter the source list
            var filteredMods = string.IsNullOrWhiteSpace(_inactiveSearchText)
                ? _allInactiveModsSource
                : _allInactiveModsSource.Where(m =>
                    m.Name.Contains(_inactiveSearchText, StringComparison.OrdinalIgnoreCase) ||
                    m.PackageId.Contains(_inactiveSearchText, StringComparison.OrdinalIgnoreCase) // Also filter by ID
                    );

            // --- Optimization: Use ReplaceAll for bulk update ---
            // Sort by Name before adding
             InactiveMods.ReplaceAll(filteredMods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
            // ----------------------------------------------------
        }
    }
}
