using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.Core.Converters.Text;

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    public class ModFilterDialogViewModel : DialogViewModelBase<ModFilterDialogResult>
    {
        private string _searchText = string.Empty;
        private string _authorFilterText = string.Empty;
        private bool? _isSupportedFilter;
        private bool? _hasAssembliesFilter;
        private bool? _hasTexturesFilter;
        private bool? _isFavoriteFilter;

        private readonly ModFilterCriteria _initialCriteria;

        // Bound Properties
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public string AuthorFilterText
        {
            get => _authorFilterText;
            set => SetProperty(ref _authorFilterText, value);
        }

        public ObservableCollection<SelectableItemViewModel<ModType>> AvailableModTypes { get; }
        public ObservableCollection<TriStateFilterItemViewModel> AvailableVersions { get; }
        public ObservableCollection<TriStateFilterItemViewModel> AvailableTags { get; }

        /// <summary>True when no mod in the library carries any local tag.</summary>
        public bool HasNoTags => AvailableTags.Count == 0;

        public ReadOnlyCollection<string> AllAvailableAuthors { get; }

public bool? IsSupportedFilter { get => _isSupportedFilter; set => SetProperty(ref _isSupportedFilter, value); }
        public bool? HasAssembliesFilter { get => _hasAssembliesFilter; set => SetProperty(ref _hasAssembliesFilter, value); }
        public bool? HasTexturesFilter { get => _hasTexturesFilter; set => SetProperty(ref _hasTexturesFilter, value); }
        public bool? IsFavoriteFilter { get => _isFavoriteFilter; set => SetProperty(ref _isFavoriteFilter, value); }

        // Commands
        public ICommand ApplyCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SetFilterPropertyCommand { get; }
        public ICommand SetFilterModeCommand { get; }

        public ModFilterDialogViewModel(
            ModFilterCriteria currentCriteria,
            IEnumerable<string> allSupportedVersions,
            IEnumerable<string> allAvailableTags,
            IEnumerable<string> allAvailableAuthors)
            : base("Filter Mods")
        {
            _initialCriteria = currentCriteria ?? new ModFilterCriteria();
            _searchText = _initialCriteria.SearchText;
            _authorFilterText = _initialCriteria.AuthorFilterText;
            _isSupportedFilter = _initialCriteria.IsSupportedFilter;
            _isFavoriteFilter = _initialCriteria.IsFavoriteFilter;
            _hasAssembliesFilter = _initialCriteria.HasAssembliesFilter;
            _hasTexturesFilter = _initialCriteria.HasTexturesFilter;

            // Populate Mod Types
            var modTypeConverter = new ModTypeToDescriptionConverter();
            AvailableModTypes = new ObservableCollection<SelectableItemViewModel<ModType>>(
                Enum.GetValues(typeof(ModType)).Cast<ModType>()
                    .Select(mt => new SelectableItemViewModel<ModType>(
                        mt,
                        (string?)modTypeConverter.Convert(mt, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture) ?? mt.ToString(),
                        _initialCriteria.SelectedModTypes?.Contains(mt) ?? false
                    ))
            );

            // Populate Versions with tri-state mode (Any/Include/Exclude)
            AvailableVersions = BuildTriStateItems(
                allSupportedVersions,
                _initialCriteria.IncludedSupportedVersions,
                _initialCriteria.ExcludedSupportedVersions,
                descending: true,
                kind: "Version");

            // Populate Tags with tri-state mode (local tags, set via Customize Mod)
            AvailableTags = BuildTriStateItems(
                allAvailableTags,
                _initialCriteria.IncludedTags,
                _initialCriteria.ExcludedTags,
                descending: false,
                kind: "Tag");

            // Store all authors
            AllAvailableAuthors = new ReadOnlyCollection<string>((allAvailableAuthors ?? Enumerable.Empty<string>()).ToList());

ApplyCommand = CreateCommand(ApplyFilters);
            ClearCommand = CreateCommand(ClearFilters);
            CancelCommand = CloseCommand;
            SetFilterPropertyCommand = CreateCommand<string>(SetFilterProperty);
            SetFilterModeCommand = CreateCommand<string>(SetFilterMode);
        }

        private static ObservableCollection<TriStateFilterItemViewModel> BuildTriStateItems(
            IEnumerable<string>? items,
            List<string>? included,
            List<string>? excluded,
            bool descending,
            string kind)
        {
            var includedSet = new HashSet<string>(included ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var excludedSet = new HashSet<string>(excluded ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var ordered = descending
                ? (items ?? Enumerable.Empty<string>()).OrderByDescending(v => v)
                : (items ?? Enumerable.Empty<string>()).OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

            return new ObservableCollection<TriStateFilterItemViewModel>(
                ordered.Select(item =>
                {
                    var mode = includedSet.Contains(item) ? TriStateFilterItemViewModel.FilterMode.Include
                             : excludedSet.Contains(item) ? TriStateFilterItemViewModel.FilterMode.Exclude
                             : TriStateFilterItemViewModel.FilterMode.Any;
                    return new TriStateFilterItemViewModel(item, mode, kind);
                })
            );
        }

        private void SetFilterProperty(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            var parts = parameter.Split('|');
            if (parts.Length != 2) return;

            string property = parts[0];
            string valueStr = parts[1];

            bool? value = valueStr switch
            {
                "True" => true,
                "False" => false,
                _ => null
            };

            switch (property)
            {
                case "IsSupported": IsSupportedFilter = value; break;
                case "IsFavorite": IsFavoriteFilter = value; break;
                case "HasAssemblies": HasAssembliesFilter = value; break;
                case "HasTextures": HasTexturesFilter = value; break;
            }
        }

        // CommandParameter format: "<Kind>|<value>|<Any|Include|Exclude>"
        // where Kind is "Version" or "Tag" to route to the right collection.
        private void SetFilterMode(string? parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            var parts = parameter.Split('|');
            if (parts.Length != 3) return;

            string kind = parts[0];
            string value = parts[1];
            var mode = parts[2] switch
            {
                "Include" => TriStateFilterItemViewModel.FilterMode.Include,
                "Exclude" => TriStateFilterItemViewModel.FilterMode.Exclude,
                _ => TriStateFilterItemViewModel.FilterMode.Any
            };

            var items = kind switch
            {
                "Version" => AvailableVersions,
                "Tag" => AvailableTags,
                _ => null
            };
            if (items == null) return;

            var item = items.FirstOrDefault(v =>
                string.Equals(v.Item, value, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.Mode = mode;
            }
        }

        private void ApplyFilters()
        {
            CurrentCriteria = new ModFilterCriteria
            {
                SearchText = this.SearchText,
                AuthorFilterText = this.AuthorFilterText,
                SelectedModTypes = AvailableModTypes.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
                IncludedSupportedVersions = AvailableVersions.Where(vm => vm.Mode == TriStateFilterItemViewModel.FilterMode.Include).Select(vm => vm.Item).ToList(),
                ExcludedSupportedVersions = AvailableVersions.Where(vm => vm.Mode == TriStateFilterItemViewModel.FilterMode.Exclude).Select(vm => vm.Item).ToList(),
                IncludedTags = AvailableTags.Where(vm => vm.Mode == TriStateFilterItemViewModel.FilterMode.Include).Select(vm => vm.Item).ToList(),
                ExcludedTags = AvailableTags.Where(vm => vm.Mode == TriStateFilterItemViewModel.FilterMode.Exclude).Select(vm => vm.Item).ToList(),
                IsSupportedFilter = this.IsSupportedFilter,
                IsFavoriteFilter = this.IsFavoriteFilter,
                HasAssembliesFilter = this.HasAssembliesFilter,
                HasTexturesFilter = this.HasTexturesFilter
            };
            CloseDialog(ModFilterDialogResult.Apply);
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            AuthorFilterText = string.Empty;
            foreach (var item in AvailableModTypes) item.IsSelected = false;
            foreach (var item in AvailableVersions) item.Mode = TriStateFilterItemViewModel.FilterMode.Any;
            foreach (var item in AvailableTags) item.Mode = TriStateFilterItemViewModel.FilterMode.Any;
            IsSupportedFilter = null;
            IsFavoriteFilter = null;
            HasAssembliesFilter = null;
            HasTexturesFilter = null;

            CurrentCriteria = new ModFilterCriteria();
            CloseDialog(ModFilterDialogResult.Clear);
        }

        public ModFilterCriteria? CurrentCriteria { get; private set; } = null;

        protected override void OnRequestCloseDialog()
        {
            if (DialogResult == default) 
            {
                DialogResult = ModFilterDialogResult.Cancel;
            }
            base.OnRequestCloseDialog();
        }
    }
}


