using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Shared.Models
{
    public class ModFilterCriteria
    {
        // Synced with the main search box
        public string SearchText { get; set; } = string.Empty;
        public string AuthorFilterText { get; set; } = string.Empty; // New: Filter by Author

        // Selected ModTypes (null or empty means no type filter)
        public List<ModType> SelectedModTypes { get; set; } = new List<ModType>();

        // Selected Supported Versions (null or empty means no version filter)
        public List<string> SelectedSupportedVersions { get; set; } = new List<string>();

        // Selected Tags (null or empty means no tag filter)
        public List<string> SelectedTags { get; set; } = new List<string>(); // New: Filter by Tags

        // Tristate filters: null = Any, true = Yes, false = No
        public bool? IsOutdatedFilter { get; set; } = null;
        public bool? HasAssembliesFilter { get; set; } = null;
        public bool? HasTexturesFilter { get; set; } = null; // <<< NEW FILTER PROPERTY >>>
        public bool? IsFavoriteFilter { get; set; } = null;

        public bool IsActive()
        {
            return !string.IsNullOrWhiteSpace(SearchText)
                || !string.IsNullOrWhiteSpace(AuthorFilterText) // Check Author
                || (SelectedModTypes != null && SelectedModTypes.Any())
                || (SelectedSupportedVersions != null && SelectedSupportedVersions.Any())
                // Check Tags
                || (SelectedTags != null && SelectedTags.Any())
                || IsOutdatedFilter.HasValue
                || IsFavoriteFilter.HasValue
                || HasAssembliesFilter.HasValue
                || HasTexturesFilter.HasValue; // <<< ADDED CHECK >>>
        }

        public void Clear()
        {
            SearchText = string.Empty;
            AuthorFilterText = string.Empty; // Clear Author
            SelectedModTypes?.Clear();
            SelectedSupportedVersions?.Clear();
            SelectedTags?.Clear(); // Clear Tags
            IsOutdatedFilter = null;
            IsFavoriteFilter = null;
            HasAssembliesFilter = null;
            HasTexturesFilter = null;
        }

        // Optional: Clone method for passing state without modifying original
        public ModFilterCriteria Clone()
        {
            return new ModFilterCriteria
            {
                SearchText = this.SearchText,
                AuthorFilterText = this.AuthorFilterText, // Clone Author
                SelectedModTypes = new List<ModType>(this.SelectedModTypes ?? Enumerable.Empty<ModType>()),
                SelectedSupportedVersions = new List<string>(this.SelectedSupportedVersions ?? Enumerable.Empty<string>()),
                SelectedTags = new List<string>(this.SelectedTags ?? Enumerable.Empty<string>()), // Clone Tags
                IsOutdatedFilter = this.IsOutdatedFilter,
                IsFavoriteFilter = this.IsFavoriteFilter,
                HasAssembliesFilter = this.HasAssembliesFilter,
                HasTexturesFilter = this.HasTexturesFilter
            };
        }
    }
}