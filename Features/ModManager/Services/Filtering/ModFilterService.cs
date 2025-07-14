using RimSharp.Core.Extensions;
using RimSharp.Core.ObservableCollections;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using FuzzySharp;

namespace RimSharp.Features.ModManager.Services.Filtering
{
    public class ModFilterService : IModFilterService
    {
        private readonly BulkObservableCollection<ModItem> _activeMods = new();
        private readonly BulkObservableCollection<ModItem> _inactiveMods = new();

        public ObservableCollection<ModItem> ActiveMods => _activeMods;
        public ObservableCollection<ModItem> InactiveMods => _inactiveMods;

        private ModFilterCriteria _activeFilterCriteria = new ModFilterCriteria();
        private ModFilterCriteria _inactiveFilterCriteria = new ModFilterCriteria();

        public ModFilterCriteria ActiveFilterCriteria => _activeFilterCriteria.Clone();
        public ModFilterCriteria InactiveFilterCriteria => _inactiveFilterCriteria.Clone();

        private IEnumerable<(ModItem Mod, int LoadOrder)> _allActiveModsSource = Array.Empty<(ModItem, int)>();
        private IEnumerable<ModItem> _allInactiveModsSource = Array.Empty<ModItem>();

        // Cached lists of available filter options
        private List<string> _allAvailableSupportedVersions = new List<string>();
        private List<string> _allAvailableTags = new List<string>();
        private List<string> _allAvailableAuthors = new List<string>();

        public string ActiveSearchText => _activeFilterCriteria.SearchText ?? string.Empty;
        public string InactiveSearchText => _inactiveFilterCriteria.SearchText ?? string.Empty;

        public IEnumerable<string> AllAvailableSupportedVersions => _allAvailableSupportedVersions;
        public IEnumerable<string> AllAvailableTags => _allAvailableTags;
        public IEnumerable<string> AllAvailableAuthors => _allAvailableAuthors;

        private const int FuzzySearchThreshold = 75; // Adjust threshold (0-100) as needed

        public void ApplyActiveFilter(string searchText)
        {
            if (_activeFilterCriteria.SearchText != searchText)
            {
                _activeFilterCriteria.SearchText = searchText ?? string.Empty;
                UpdateActiveModsCollection();
            }
        }

        public void ApplyInactiveFilter(string searchText)
        {
            if (_inactiveFilterCriteria.SearchText != searchText)
            {
                _inactiveFilterCriteria.SearchText = searchText ?? string.Empty;
                UpdateInactiveModsCollection();
            }
        }

        public void ApplyActiveFilterCriteria(ModFilterCriteria criteria)
        {
            _activeFilterCriteria = criteria ?? new ModFilterCriteria();
            UpdateActiveModsCollection();
        }

        public void ApplyInactiveFilterCriteria(ModFilterCriteria criteria)
        {
            _inactiveFilterCriteria = criteria ?? new ModFilterCriteria();
            UpdateInactiveModsCollection();
        }

        public void ClearActiveFilters()
        {
            ApplyActiveFilterCriteria(new ModFilterCriteria());
        }
        public void ClearInactiveFilters()
        {
            ApplyInactiveFilterCriteria(new ModFilterCriteria());
        }

        public void UpdateCollections(IEnumerable<(ModItem Mod, int LoadOrder)> activeMods, IEnumerable<ModItem> inactiveMods)
        {
            _allActiveModsSource = activeMods?.ToList() ?? throw new ArgumentNullException(nameof(activeMods));
            _allInactiveModsSource = inactiveMods?.ToList() ?? throw new ArgumentNullException(nameof(inactiveMods));

            var allMods = _allActiveModsSource.Select(m => m.Mod).Concat(_allInactiveModsSource);

            // Update available versions list
            _allAvailableSupportedVersions = allMods
                                              .SelectMany(m => m.SupportedVersionStrings)
                                              .Distinct(StringComparer.OrdinalIgnoreCase)
                                              .OrderByDescending(v => v)
                                              .ToList();

            // Update available tags list
            _allAvailableTags = allMods
                                  .SelectMany(m => m.TagList) // Use parsed TagList
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                  .ToList();

            // Update available authors list
            _allAvailableAuthors = allMods
                                     .SelectMany(m => m.AuthorList) // Use parsed AuthorList
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

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

            var filteredMods = FilterMods(_allActiveModsSource.Select(x => x.Mod), _activeFilterCriteria)
                               .OrderBy(mod => _allActiveModsSource.FirstOrDefault(x => x.Mod.PackageId == mod.PackageId).LoadOrder)
                               .ToList();
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

            var filteredMods = FilterMods(_allInactiveModsSource, _inactiveFilterCriteria)
                                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            _inactiveMods.ReplaceAll(filteredMods);
        }

        private IEnumerable<ModItem> FilterMods(IEnumerable<ModItem> source, ModFilterCriteria criteria)
        {
            IEnumerable<ModItem> filtered = source;

            // 1. Text Search (Name/PackageId) - Now with improved Fuzzy Matching
            if (!string.IsNullOrWhiteSpace(criteria.SearchText))
            {
                string searchLower = criteria.SearchText.ToLowerInvariant();
                filtered = filtered.Where(m =>
                    // Tier 1: Standard 'Contains' search
                    (m.Name?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (m.PackageId?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (m.Description?.ToLowerInvariant().Contains(searchLower) ?? false) ||

                    // Tier 2: Fuzzy search on token sets
                    Fuzz.TokenSetRatio(m.Name?.ToLowerInvariant() ?? "", searchLower) >= FuzzySearchThreshold
                );
            }

            // 2. Author Filter
            if (!string.IsNullOrWhiteSpace(criteria.AuthorFilterText))
            {
                string authorLower = criteria.AuthorFilterText.ToLowerInvariant();
                filtered = filtered.Where(m =>
                    m.AuthorList.Any(a => a.ToLowerInvariant().Contains(authorLower))
                );
            }

            // 3. Mod Type Filter
            if (criteria.SelectedModTypes?.Any() ?? false)
            {
                var selectedTypesSet = new HashSet<ModType>(criteria.SelectedModTypes);
                filtered = filtered.Where(m => selectedTypesSet.Contains(m.ModType));
            }

            // 4. Supported Version Filter
            if (criteria.SelectedSupportedVersions?.Any() ?? false)
            {
                var selectedVersionsSet = new HashSet<string>(criteria.SelectedSupportedVersions, StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(m => m.SupportedVersionStrings.Any(sv => selectedVersionsSet.Contains(sv)));
            }

            // 5. Tag Filter
            if (criteria.SelectedTags?.Any() ?? false)
            {
                var selectedTagsSet = new HashSet<string>(criteria.SelectedTags, StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(m => m.TagList.Any(tag => selectedTagsSet.Contains(tag)));
            }

            // 6. Boolean Filters (Tristate)
            if (criteria.IsOutdatedFilter.HasValue)
            {
                filtered = filtered.Where(m => m.IsOutdatedRW == criteria.IsOutdatedFilter.Value);
            }
            if (criteria.IsFavoriteFilter.HasValue)
            {
                filtered = filtered.Where(m => m.IsFavorite == criteria.IsFavoriteFilter.Value);
            }
            if (criteria.HasAssembliesFilter.HasValue)
            {
                filtered = filtered.Where(m => m.Assemblies == criteria.HasAssembliesFilter.Value);
            }

            return filtered;
        }
    }
}
