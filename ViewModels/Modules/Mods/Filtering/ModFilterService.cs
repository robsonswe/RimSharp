using RimSharp.Models;
using RimSharp.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RimSharp.ViewModels.Modules.Mods.Filtering
{
    public class ModFilterService : IModFilterService
    {
        // Store optimized collections for better performance
        private readonly BulkObservableCollection<ModItem> _activeMods = new();
        private readonly BulkObservableCollection<ModItem> _inactiveMods = new();
        
        // Implement interface properties that expose the collections
        public ObservableCollection<ModItem> ActiveMods => _activeMods;
        public ObservableCollection<ModItem> InactiveMods => _inactiveMods;

        // Filter state
        private string _activeSearchText = string.Empty;
        private string _inactiveSearchText = string.Empty;
        
        public string ActiveSearchText => _activeSearchText;
        public string InactiveSearchText => _inactiveSearchText;

        // Cache source collections
        private IEnumerable<(ModItem Mod, int LoadOrder)> _allActiveModsSource = Array.Empty<(ModItem, int)>();
        private IEnumerable<ModItem> _allInactiveModsSource = Array.Empty<ModItem>();

        public void ApplyActiveFilter(string searchText)
        {
            _activeSearchText = searchText ?? string.Empty;
            UpdateActiveModsCollection();
        }

        public void ApplyInactiveFilter(string searchText)
        {
            _inactiveSearchText = searchText ?? string.Empty;
            UpdateInactiveModsCollection();
        }

        public void UpdateCollections(IEnumerable<(ModItem Mod, int LoadOrder)> activeMods, IEnumerable<ModItem> inactiveMods)
        {
            _allActiveModsSource = activeMods ?? throw new ArgumentNullException(nameof(activeMods));
            _allInactiveModsSource = inactiveMods ?? throw new ArgumentNullException(nameof(inactiveMods));

            UpdateActiveModsCollection();
            UpdateInactiveModsCollection();
        }

        private void UpdateActiveModsCollection()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher?.CheckAccess() == false)
            {
                dispatcher.SafeInvoke(UpdateActiveModsCollection);
                return;
            }

            var filteredMods = FilterActiveMods().ToList();
            _activeMods.ReplaceAll(filteredMods);
        }

        private void UpdateInactiveModsCollection()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher?.CheckAccess() == false)
            {
                dispatcher.SafeInvoke(UpdateInactiveModsCollection);
                return;
            }

            var filteredMods = FilterInactiveMods().ToList();
            _inactiveMods.ReplaceAll(filteredMods);
        }

        private IEnumerable<ModItem> FilterActiveMods()
        {
            // If no filter, just sort and return
            if (string.IsNullOrWhiteSpace(_activeSearchText))
            {
                return _allActiveModsSource.OrderBy(x => x.LoadOrder).Select(x => x.Mod);
            }

            // Apply filter, sort, and return
            return _allActiveModsSource
                .Where(x => ContainsTextIgnoreCase(x.Mod.Name, _activeSearchText) || 
                           ContainsTextIgnoreCase(x.Mod.PackageId, _activeSearchText))
                .OrderBy(x => x.LoadOrder)
                .Select(x => x.Mod);
        }

        private IEnumerable<ModItem> FilterInactiveMods()
        {
            // If no filter, just sort and return
            if (string.IsNullOrWhiteSpace(_inactiveSearchText))
            {
                return _allInactiveModsSource.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
            }

            // Apply filter, sort, and return
            return _allInactiveModsSource
                .Where(m => ContainsTextIgnoreCase(m.Name, _inactiveSearchText) || 
                           ContainsTextIgnoreCase(m.PackageId, _inactiveSearchText))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
        }

        // Optimized string comparison that handles nulls safely
        private static bool ContainsTextIgnoreCase(string source, string searchText)
        {
            return !string.IsNullOrEmpty(source) && 
                   source.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }
    }
}