using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles; // For ViewModelBase
using RimSharp.AppDir.Dialogs;
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
        public ICommand SelectByTimeframeCommand { get; } // <<< NEW COMMAND
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

        // Sort state indicators (used for UI feedback, e.g., arrows)
        private string _nameSortState = "SortedAscending"; // Default sort
        public string NameSortState { get => _nameSortState; private set => SetProperty(ref _nameSortState, value); }
        private string _packageIdSortState = "";
        public string PackageIdSortState { get => _packageIdSortState; private set => SetProperty(ref _packageIdSortState, value); }
        private string _steamIdSortState = "";
        public string SteamIdSortState { get => _steamIdSortState; private set => SetProperty(ref _steamIdSortState, value); }
        private string _localUpdateDateSortState = "";
        public string LocalUpdateDateSortState { get => _localUpdateDateSortState; private set => SetProperty(ref _localUpdateDateSortState, value); }

        private bool _hasSelectedItems;
        public bool HasSelectedItems
        {
            get => _hasSelectedItems;
            private set => SetProperty(ref _hasSelectedItems, value);
        }
        
        // <<< NEW PROPERTIES FOR SELECTION COUNTER >>>
        public int SelectedModCount => _modsToCheck.Count(m => m.IsSelected);
        public int TotalModCount => _modsToCheck.Count;

        public UpdateCheckDialogViewModel(IEnumerable<ModItem> workshopMods)
            : base("Check Mod Updates")
        {
             var checkItems = workshopMods
                 .Select(mod => new UpdateCheckItemViewModel(mod));
             _modsToCheck = new ObservableCollection<UpdateCheckItemViewModel>(checkItems);

            ModsView = CollectionViewSource.GetDefaultView(_modsToCheck);
            ApplySort();

            SelectAllCommand = CreateCommand(ExecuteSelectAll);
            SelectNoneCommand = CreateCommand(ExecuteSelectNone);
            SelectActiveCommand = CreateCommand(ExecuteSelectActive);
            SelectByTimeframeCommand = CreateCommand<string>(ExecuteSelectByTimeframe); // <<< NEW COMMAND INITIALIZATION
            CancelCommand = CreateCommand(() => CloseDialog(UpdateCheckDialogResult.Cancel));
            SortCommand = CreateCommand<string>(ExecuteSort);

            UpdateCommand = CreateCommand(
                () => CloseDialog(UpdateCheckDialogResult.CheckUpdates),
                () => HasSelectedItems,
                nameof(HasSelectedItems)
            );

            foreach (var item in _modsToCheck)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
            
            UpdateSelectionState();
            UpdateSortIndicators();
            OnPropertyChanged(nameof(TotalModCount)); // <<< INITIALIZE TOTAL COUNT
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
            OnPropertyChanged(nameof(SelectedModCount)); // <<< NOTIFY COUNTER UPDATE
        }

        // <<< NEW: Helper for performant batch selections >>>
        private void BatchUpdateSelection(Func<UpdateCheckItemViewModel, bool> selectionCriteria)
        {
            foreach (var item in _modsToCheck) item.PropertyChanged -= Item_PropertyChanged;
            
            foreach (var item in _modsToCheck)
            {
                item.IsSelected = selectionCriteria(item);
            }
            
            foreach (var item in _modsToCheck) item.PropertyChanged += Item_PropertyChanged;
            
            UpdateSelectionState();
        }
        
        // <<< REFACTORED selection methods >>>
        private void ExecuteSelectAll() => BatchUpdateSelection(item => true);

        private void ExecuteSelectNone() => BatchUpdateSelection(item => false);

        private void ExecuteSelectActive() => BatchUpdateSelection(item => item.Mod?.IsActive ?? false);
        
        // <<< NEW: Logic for smart selection >>>
        private void ExecuteSelectByTimeframe(string timeframe)
        {
            var now = DateTime.Now;
            switch (timeframe)
            {
                case "outdated":
                    BatchUpdateSelection(item => item.Mod?.IsOutdatedRW ?? false);
                    break;
                case "week":
                    var cutoffWeek = now.AddDays(-7);
                    BatchUpdateSelection(item => item.LocalUpdateDateTime < cutoffWeek && item.LocalUpdateDateTime != DateTime.MinValue);
                    break;
                case "month":
                    var cutoffMonth = now.AddMonths(-1);
                    BatchUpdateSelection(item => item.LocalUpdateDateTime < cutoffMonth && item.LocalUpdateDateTime != DateTime.MinValue);
                    break;
                case "6months":
                    var cutoff6Months = now.AddMonths(-6);
                    BatchUpdateSelection(item => item.LocalUpdateDateTime < cutoff6Months && item.LocalUpdateDateTime != DateTime.MinValue);
                    break;
                case "year":
                    var cutoffYear = now.AddYears(-1);
                    BatchUpdateSelection(item => item.LocalUpdateDateTime < cutoffYear && item.LocalUpdateDateTime != DateTime.MinValue);
                    break;
            }
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
                case UpdateCheckItemViewModel.LocalUpdateDateTimePropertyName: LocalUpdateDateSortState = sortState; break;
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