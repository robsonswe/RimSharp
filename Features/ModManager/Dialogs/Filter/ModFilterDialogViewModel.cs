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
        private string _searchText;
        private string _authorFilterText; // New
        private bool? _isOutdatedFilter;
        private bool? _hasAssembliesFilter; // <<< NEW FIELD >>>
        private bool? _isFavoriteFilter;

        private readonly ModFilterCriteria _initialCriteria;

        // Bound Properties
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public string AuthorFilterText // New
        {
            get => _authorFilterText;
            set => SetProperty(ref _authorFilterText, value);
        }

        public ObservableCollection<SelectableItemViewModel<ModType>> AvailableModTypes { get; }
        public ObservableCollection<SelectableItemViewModel<string>> AvailableVersions { get; }
        public ObservableCollection<SelectableItemViewModel<string>> AvailableTags { get; } // New

        // Store all authors for potential future autocomplete suggestions
        public ReadOnlyCollection<string> AllAvailableAuthors { get; } // New


        public bool? IsOutdatedFilter { get => _isOutdatedFilter; set => SetProperty(ref _isOutdatedFilter, value); }
        public bool? HasAssembliesFilter { get => _hasAssembliesFilter; set => SetProperty(ref _hasAssembliesFilter, value); } // <<< NEW PROPERTY >>>
        public bool? IsFavoriteFilter { get => _isFavoriteFilter; set => SetProperty(ref _isFavoriteFilter, value); }

        // Commands
        public ICommand ApplyCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CancelCommand { get; } // Uses base CloseCommand

        public ModFilterDialogViewModel(
            ModFilterCriteria currentCriteria,
            IEnumerable<string> allSupportedVersions,
            IEnumerable<string> allAvailableTags, // New
            IEnumerable<string> allAvailableAuthors) // New
            : base("Filter Mods")
        {
            _initialCriteria = currentCriteria ?? new ModFilterCriteria();

            // Initialize properties from current criteria
            _searchText = _initialCriteria.SearchText;
            _authorFilterText = _initialCriteria.AuthorFilterText; // New
            _isOutdatedFilter = _initialCriteria.IsOutdatedFilter;
            _isFavoriteFilter = _initialCriteria.IsFavoriteFilter;
            _hasAssembliesFilter = _initialCriteria.HasAssembliesFilter; // <<< INITIALIZE NEW FILTER >>>

            // Populate Mod Types
            var modTypeConverter = new ModTypeToDescriptionConverter();
            AvailableModTypes = new ObservableCollection<SelectableItemViewModel<ModType>>(
                Enum.GetValues(typeof(ModType)).Cast<ModType>()
                    .Select(mt => new SelectableItemViewModel<ModType>(
                        mt,
                        (string)modTypeConverter.Convert(mt, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture),
                        _initialCriteria.SelectedModTypes?.Contains(mt) ?? false
                    ))
            );

            // Populate Versions
            AvailableVersions = new ObservableCollection<SelectableItemViewModel<string>>(
                (allSupportedVersions ?? Enumerable.Empty<string>())
                    .OrderByDescending(v => v)
                    .Select(v => new SelectableItemViewModel<string>(
                        v, v, _initialCriteria.SelectedSupportedVersions?.Contains(v) ?? false
                    ))
            );

            // Populate Tags (New)
            AvailableTags = new ObservableCollection<SelectableItemViewModel<string>>(
                (allAvailableTags ?? Enumerable.Empty<string>())
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new SelectableItemViewModel<string>(
                        t, t, _initialCriteria.SelectedTags?.Contains(t, StringComparer.OrdinalIgnoreCase) ?? false
                    ))
            );

            // Store all authors
            AllAvailableAuthors = new ReadOnlyCollection<string>((allAvailableAuthors ?? Enumerable.Empty<string>()).ToList());


            // Initialize Commands
            ApplyCommand = CreateCommand(ApplyFilters);
            ClearCommand = CreateCommand(ClearFilters);
            CancelCommand = CloseCommand; // Use base CloseCommand for Cancel
        }

        private void ApplyFilters()
        {
            CurrentCriteria = new ModFilterCriteria
            {
                SearchText = this.SearchText,
                AuthorFilterText = this.AuthorFilterText, // New
                SelectedModTypes = AvailableModTypes.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
                SelectedSupportedVersions = AvailableVersions.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
                SelectedTags = AvailableTags.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(), // New
                IsOutdatedFilter = this.IsOutdatedFilter,
                IsFavoriteFilter = this.IsFavoriteFilter,
                HasAssembliesFilter = this.HasAssembliesFilter // <<< APPLY NEW FILTER >>>
            };
            CloseDialog(ModFilterDialogResult.Apply);
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            AuthorFilterText = string.Empty; // New
            foreach (var item in AvailableModTypes) item.IsSelected = false;
            foreach (var item in AvailableVersions) item.IsSelected = false;
            foreach (var item in AvailableTags) item.IsSelected = false; // New
            IsOutdatedFilter = null;
            IsFavoriteFilter = null;
            HasAssembliesFilter = null; // <<< CLEAR NEW FILTER >>>

            CurrentCriteria = new ModFilterCriteria(); // Cleared criteria
            CloseDialog(ModFilterDialogResult.Clear);
        }

        public ModFilterCriteria CurrentCriteria { get; private set; } = null;

        protected override void OnRequestCloseDialog()
        {
            if (DialogResult == default) // default is Cancel (0)
            {
                DialogResult = ModFilterDialogResult.Cancel;
            }
            base.OnRequestCloseDialog();
        }
    }
}
