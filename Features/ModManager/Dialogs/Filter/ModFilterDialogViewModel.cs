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
        public ObservableCollection<VersionFilterItemViewModel> AvailableVersions { get; }
        public ObservableCollection<SelectableItemViewModel<string>> AvailableTags { get; }

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
        public ICommand SetVersionModeCommand { get; }

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
            var included = new HashSet<string>(_initialCriteria.IncludedSupportedVersions ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(_initialCriteria.ExcludedSupportedVersions ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            AvailableVersions = new ObservableCollection<VersionFilterItemViewModel>(
                (allSupportedVersions ?? Enumerable.Empty<string>())
                    .OrderByDescending(v => v)
                    .Select(v =>
                    {
                        var mode = included.Contains(v) ? VersionFilterItemViewModel.VersionMode.Include
                                 : excluded.Contains(v) ? VersionFilterItemViewModel.VersionMode.Exclude
                                 : VersionFilterItemViewModel.VersionMode.Any;
                        return new VersionFilterItemViewModel(v, mode);
                    })
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

ApplyCommand = CreateCommand(ApplyFilters);
            ClearCommand = CreateCommand(ClearFilters);
            CancelCommand = CloseCommand;
            SetFilterPropertyCommand = CreateCommand<string>(SetFilterProperty);
            SetVersionModeCommand = CreateCommand<string>(SetVersionMode);
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

        // CommandParameter format: "<version>|<Any|Include|Exclude>"
        private void SetVersionMode(string? parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;

            var parts = parameter.Split('|');
            if (parts.Length != 2) return;

            string version = parts[0];
            var mode = parts[1] switch
            {
                "Include" => VersionFilterItemViewModel.VersionMode.Include,
                "Exclude" => VersionFilterItemViewModel.VersionMode.Exclude,
                _ => VersionFilterItemViewModel.VersionMode.Any
            };

            var item = AvailableVersions.FirstOrDefault(v =>
                string.Equals(v.Item, version, StringComparison.OrdinalIgnoreCase));
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
                IncludedSupportedVersions = AvailableVersions.Where(vm => vm.Mode == VersionFilterItemViewModel.VersionMode.Include).Select(vm => vm.Item).ToList(),
                ExcludedSupportedVersions = AvailableVersions.Where(vm => vm.Mode == VersionFilterItemViewModel.VersionMode.Exclude).Select(vm => vm.Item).ToList(),
                SelectedTags = AvailableTags.Where(vm => vm.IsSelected).Select(vm => vm.Item).ToList(),
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
            foreach (var item in AvailableVersions) item.Mode = VersionFilterItemViewModel.VersionMode.Any;
            foreach (var item in AvailableTags) item.IsSelected = false;
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


