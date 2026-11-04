using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Shared.Models
{
    public class ModFilterCriteria
    {
        // Synced with the main search box
        public string SearchText { get; set; } = string.Empty;
        public string AuthorFilterText { get; set; } = string.Empty;

        public List<ModType> SelectedModTypes { get; set; } = new List<ModType>();

        // Version filter with two complementary lists (Any / Include / Exclude).
        // A mod matches when it supports at least one INCLUDED version (if any
        // are set) AND none of the EXCLUDED versions (if any are set).
        public List<string> IncludedSupportedVersions { get; set; } = new List<string>();
        public List<string> ExcludedSupportedVersions { get; set; } = new List<string>();

        public List<string> SelectedTags { get; set; } = new List<string>();

        public bool? IsSupportedFilter { get; set; } = null;
        public bool? HasAssembliesFilter { get; set; } = null;
        public bool? HasTexturesFilter { get; set; } = null;
        public bool? IsFavoriteFilter { get; set; } = null;

        public bool IsActive()
        {
            return !string.IsNullOrWhiteSpace(SearchText)
                || !string.IsNullOrWhiteSpace(AuthorFilterText)
                || (SelectedModTypes != null && SelectedModTypes.Any())
                || (IncludedSupportedVersions != null && IncludedSupportedVersions.Any())
                || (ExcludedSupportedVersions != null && ExcludedSupportedVersions.Any())
                || (SelectedTags != null && SelectedTags.Any())
                || IsSupportedFilter.HasValue
                || IsFavoriteFilter.HasValue
                || HasAssembliesFilter.HasValue
                || HasTexturesFilter.HasValue;
        }

        public void Clear()
        {
            SearchText = string.Empty;
            AuthorFilterText = string.Empty;
            SelectedModTypes?.Clear();
            IncludedSupportedVersions?.Clear();
            ExcludedSupportedVersions?.Clear();
            SelectedTags?.Clear(); 
            IsSupportedFilter = null;
            IsFavoriteFilter = null;
            HasAssembliesFilter = null;
            HasTexturesFilter = null;
        }

        public ModFilterCriteria Clone()
        {
            return new ModFilterCriteria
            {
                SearchText = this.SearchText,
                AuthorFilterText = this.AuthorFilterText, // Clone Author
                SelectedModTypes = new List<ModType>(this.SelectedModTypes ?? Enumerable.Empty<ModType>()),
                IncludedSupportedVersions = new List<string>(this.IncludedSupportedVersions ?? Enumerable.Empty<string>()),
                ExcludedSupportedVersions = new List<string>(this.ExcludedSupportedVersions ?? Enumerable.Empty<string>()),
                SelectedTags = new List<string>(this.SelectedTags ?? Enumerable.Empty<string>()), // Clone Tags
                IsSupportedFilter = this.IsSupportedFilter,
                IsFavoriteFilter = this.IsFavoriteFilter,
                HasAssembliesFilter = this.HasAssembliesFilter,
                HasTexturesFilter = this.HasTexturesFilter
            };
        }
    }
}


