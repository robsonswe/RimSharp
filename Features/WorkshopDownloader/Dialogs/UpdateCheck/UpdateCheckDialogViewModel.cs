using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RimSharp.MyApp.AppFiles; // For ViewModelBase
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;

namespace RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck
{
    public enum UpdateCheckDialogResult
    {
        Cancel,
        CheckUpdates
    }

    public class UpdateCheckDialogViewModel : DialogViewModelBase<UpdateCheckDialogResult>
    {
        private readonly ObservableCollection<UpdateCheckItemViewModel> _modsToCheck;
        public ICollectionView ModsView { get; }

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SelectActiveCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SortCommand { get; }

        private string _currentSortProperty = UpdateCheckItemViewModel.NamePropertyName;
        public string CurrentSortProperty
        {
            get => _currentSortProperty;
            private set => SetProperty(ref _currentSortProperty, value);
        }

        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
        public ListSortDirection CurrentSortDirection
        {
            get => _currentSortDirection;
            private set => SetProperty(ref _currentSortDirection, value);
        }

        private string _nameSortState = "SortedAscending";
        public string NameSortState { get => _nameSortState; private set => SetProperty(ref _nameSortState, value); }
        private string _packageIdSortState = "";
        public string PackageIdSortState { get => _packageIdSortState; private set => SetProperty(ref _packageIdSortState, value); }
        private string _steamIdSortState = "";
        public string SteamIdSortState { get => _steamIdSortState; private set => SetProperty(ref _steamIdSortState, value); }
        private string _localUpdateDateSortState = "";
        public string LocalUpdateDateSortState { get => _localUpdateDateSortState; private set => SetProperty(ref _localUpdateDateSortState, value); }

        // Track selection state to simplify CanExecute
        private bool _hasSelectedItems;
        public bool HasSelectedItems
        {
            get => _hasSelectedItems;
            private set => SetProperty(ref _hasSelectedItems, value);
        }

        public UpdateCheckDialogViewModel(IEnumerable<ModItem> workshopMods)
            : base("Check Mod Updates")
        {
            var checkItems = workshopMods
                .Select(mod => new UpdateCheckItemViewModel(mod))
                .OrderBy(vm => vm.Name);
            _modsToCheck = new ObservableCollection<UpdateCheckItemViewModel>(checkItems);

            ModsView = CollectionViewSource.GetDefaultView(_modsToCheck);

            // Initialize commands using ViewModelBase helpers
            SelectAllCommand = CreateCommand(ExecuteSelectAll);
            SelectNoneCommand = CreateCommand(ExecuteSelectNone);
            SelectActiveCommand = CreateCommand(ExecuteSelectActive);
            CancelCommand = CreateCommand(() => CloseDialog(UpdateCheckDialogResult.Cancel));
            SortCommand = CreateCommand<string>(ExecuteSort);

            // UpdateCommand observes HasSelectedItems
            UpdateCommand = CreateCommand(
                () => CloseDialog(UpdateCheckDialogResult.CheckUpdates),
                () => HasSelectedItems,
                nameof(HasSelectedItems)
            );

            // Subscribe to item changes to update HasSelectedItems
            foreach (var item in _modsToCheck)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
            // Initialize selection state
            UpdateSelectionState();
            // Update sort indicators
            UpdateSortIndicators();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == UpdateCheckItemViewModel.IsSelectedPropertyName)
            {
                UpdateSelectionState();
            }
        }

        private void UpdateSelectionState()
        {
            HasSelectedItems = _modsToCheck.Any(item => item.IsSelected);
        }

        private void ExecuteSelectAll()
        {
            foreach (var item in _modsToCheck) item.IsSelected = true;
        }

        private void ExecuteSelectNone()
        {
            foreach (var item in _modsToCheck) item.IsSelected = false;
        }

        private void ExecuteSelectActive()
        {
            foreach (var item in _modsToCheck) item.IsSelected = item.Mod?.IsActive ?? false;
        }

        private void ExecuteSort(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return;

            if (CurrentSortProperty == propertyName)
            {
                CurrentSortDirection = (CurrentSortDirection == ListSortDirection.Ascending)
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            else
            {
                CurrentSortProperty = propertyName;
                CurrentSortDirection = ListSortDirection.Ascending;
            }
            ApplySort();
        }

        private void ApplySort()
        {
            using (ModsView.DeferRefresh())
            {
                ModsView.SortDescriptions.Clear();
                ModsView.SortDescriptions.Add(new SortDescription(CurrentSortProperty, CurrentSortDirection));
            }
            UpdateSortIndicators();
        }

        private void UpdateSortIndicators()
        {
            NameSortState = ""; PackageIdSortState = ""; SteamIdSortState = ""; LocalUpdateDateSortState = "";
            string sortState = CurrentSortDirection == ListSortDirection.Ascending ? "SortedAscending" : "SortedDescending";

            switch (CurrentSortProperty)
            {
                case UpdateCheckItemViewModel.NamePropertyName: NameSortState = sortState; break;
                case UpdateCheckItemViewModel.PackageIdPropertyName: PackageIdSortState = sortState; break;
                case UpdateCheckItemViewModel.SteamIdPropertyName: SteamIdSortState = sortState; break;
                case UpdateCheckItemViewModel.LocalUpdateDatePropertyName: LocalUpdateDateSortState = sortState; break;
            }
        }

        public IEnumerable<ModItem> GetSelectedMods()
        {
            return _modsToCheck.Where(vm => vm.IsSelected).Select(vm => vm.Mod);
        }

        public void Cleanup()
        {
            foreach (var item in _modsToCheck)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }
    }
}