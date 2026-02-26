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
        private bool? _isOutdatedFilter;
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
        public ObservableCollection<SelectableItemViewModel<string>> AvailableVersions { get; }
        public ObservableCollection<SelectableItemViewModel<string>> AvailableTags { get; }

        public ReadOnlyCollection<string> AllAvailableAuthors { get; }


        public bool? IsOutdatedFilter { get => _isOutdatedFilter; set => SetProperty(ref _isOutdatedFilter, value); }
        public bool? HasAssembliesFilter { get => _hasAssembliesFilter; set => SetProperty(ref _hasAssembliesFilter, value); }
        public bool? HasTexturesFilter { get => _hasTexturesFilter; set => SetProperty(ref _hasTexturesFilter, value); }
        public bool? IsFavoriteFilter { get => _isFavoriteFilter; set => SetProperty(ref _isFavoriteFilter, value); }

        // Commands
        public ICommand ApplyCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SetFilterPropertyCommand { get; } // New command to fix RadioButton behavior

        public ModFilterDialogViewModel(
            ModFilterCriteria currentCriteria,
            IEnumerable<string> allSupportedVersions,
            IEnumerable<string> allAvailableTags,
            IEnumerable<string> allAvailableAuthors)
            : base("Filter Mods")
        {
            _initialCriteria = currentCriteria ?? new ModFilterCriteria();

            // Initialize properties from current criteria
            _searchText = _initialCriteria.SearchText;
            _authorFilterText = _initialCriteria.AuthorFilterText;
            _isOutdatedFilter = _initialCriteria.IsOutdatedFilter;
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

            // Populate Versions
            AvailableVersions = new ObservableCollection<SelectableItemViewModel<string>>(
                (allSupportedVersions ?? Enumerable.Empty<string>())
                    .OrderByDescending(v => v)
                    .Select(v => new SelectableItemViewModel<string>(
                        v, v, _initialCriteria.SelectedSupportedVersions?.Contains(v) ?? false
                    ))
            );

            // Populate Tags
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
            CancelCommand = CloseCommand;
            SetFilterPropertyCommand = CreateCommand<string>(SetFilterProperty);
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
                case "IsOutdated": IsOutdatedFilter = value; break;
                case "IsFavorite": IsFavoriteFilter = value; break;
                case "HasAssemblies": HasAssembliesFilter = value; break;
                case "HasTextures": HasTexturesFilter = value; break;
            }
        }

        private void ApplyFilters()
        {
            CurrentCriteria = new ModFilterCriteria
            {
                SearchText = this.SearchText,
                AuthorFilterText = this.AuthorFilterText,
                SelectedModTypes = AvailableModTypes.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
                SelectedSupportedVersions = AvailableVersions.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
                SelectedTags = AvailableTags.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
                IsOutdatedFilter = this.IsOutdatedFilter,
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
            foreach (var item in AvailableVersions) item.IsSelected = false;
            foreach (var item in AvailableTags) item.IsSelected = false;
            IsOutdatedFilter = null;
            IsFavoriteFilter = null;
            HasAssembliesFilter = null;
            HasTexturesFilter = null;

            CurrentCriteria = new ModFilterCriteria();
            CloseDialog(ModFilterDialogResult.Clear);
        }

        public ModFilterCriteria? CurrentCriteria { get; private set; } = null;

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