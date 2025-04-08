using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RimSharp.Core.Commands;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    public enum UpdateCheckDialogResult
    {
        Cancel,
        CheckUpdates
    }

    public class UpdateCheckDialogViewModel : DialogViewModelBase<UpdateCheckDialogResult>
    {
        // Store the original collection
        private readonly ObservableCollection<UpdateCheckItemViewModel> _modsToCheck;

        // Expose the view for binding
        public ICollectionView ModsView { get; }

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SortCommand { get; }

        // Add properties to track current sort state (for UI binding)
        private string _currentSortProperty = nameof(UpdateCheckItemViewModel.Name); // Default sort
        public string CurrentSortProperty 
        {
            get => _currentSortProperty;
            private set 
            {
                if (_currentSortProperty != value)
                {
                    _currentSortProperty = value;
                    OnPropertyChanged(nameof(CurrentSortProperty));
                    UpdateSortIndicators();
                }
            }
        }

        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
        public ListSortDirection CurrentSortDirection
        {
            get => _currentSortDirection;
            private set
            {
                if (_currentSortDirection != value)
                {
                    _currentSortDirection = value;
                    OnPropertyChanged(nameof(CurrentSortDirection));
                    UpdateSortIndicators();
                }
            }
        }

        // Properties for each column's sort state
        private string _nameSortState = "SortedAscending"; // Default initial sort
        public string NameSortState
        {
            get => _nameSortState;
            private set
            {
                if (_nameSortState != value)
                {
                    _nameSortState = value;
                    OnPropertyChanged(nameof(NameSortState));
                }
            }
        }

        private string _packageIdSortState = "";
        public string PackageIdSortState
        {
            get => _packageIdSortState;
            private set
            {
                if (_packageIdSortState != value)
                {
                    _packageIdSortState = value;
                    OnPropertyChanged(nameof(PackageIdSortState));
                }
            }
        }

        private string _steamIdSortState = "";
        public string SteamIdSortState
        {
            get => _steamIdSortState;
            private set
            {
                if (_steamIdSortState != value)
                {
                    _steamIdSortState = value;
                    OnPropertyChanged(nameof(SteamIdSortState));
                }
            }
        }

        private string _localUpdateDateSortState = "";
        public string LocalUpdateDateSortState
        {
            get => _localUpdateDateSortState;
            private set
            {
                if (_localUpdateDateSortState != value)
                {
                    _localUpdateDateSortState = value;
                    OnPropertyChanged(nameof(LocalUpdateDateSortState));
                }
            }
        }

        public UpdateCheckDialogViewModel(IEnumerable<ModItem> workshopMods)
            : base("Check Mod Updates")
        {
            var checkItems = workshopMods.Select(mod => new UpdateCheckItemViewModel(mod));
            _modsToCheck = new ObservableCollection<UpdateCheckItemViewModel>(checkItems);

            // Create the CollectionViewSource and get the view
            ModsView = CollectionViewSource.GetDefaultView(_modsToCheck);
            ApplySort(); // Apply initial sort

            SelectAllCommand = new RelayCommand(ExecuteSelectAll);
            SelectNoneCommand = new RelayCommand(ExecuteSelectNone);
            SortCommand = new RelayCommand(ExecuteSort);

            UpdateCommand = new RelayCommand(
                _ => CloseDialog(UpdateCheckDialogResult.CheckUpdates),
                CanExecuteUpdate
             );

            CancelCommand = new RelayCommand(
                 _ => CloseDialog(UpdateCheckDialogResult.Cancel)
             );

             // Monitor selection changes on the original collection items
             foreach(var item in _modsToCheck)
             {
                 item.PropertyChanged += (s,e) => {
                     if (e.PropertyName == nameof(UpdateCheckItemViewModel.IsSelected))
                     {
                         ((RelayCommand)UpdateCommand).RaiseCanExecuteChanged();
                     }
                 };
             }
        }

        private void ExecuteSelectAll(object parameter)
        {
            foreach (var item in _modsToCheck)
            {
                item.IsSelected = true;
            }
        }

        private void ExecuteSelectNone(object parameter)
        {
            foreach (var item in _modsToCheck)
            {
                item.IsSelected = false;
            }
        }

        private bool CanExecuteUpdate(object parameter)
        {
            return _modsToCheck.Any(item => item.IsSelected);
        }

        private void ExecuteSort(object parameter)
        {
            if (parameter is string propertyName)
            {
                if (CurrentSortProperty == propertyName)
                {
                    // Toggle direction if sorting by the same property
                    CurrentSortDirection = (CurrentSortDirection == ListSortDirection.Ascending)
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                }
                else
                {
                    // New property, sort ascending
                    CurrentSortProperty = propertyName;
                    CurrentSortDirection = ListSortDirection.Ascending;
                }

                ApplySort();
            }
        }

        private void ApplySort()
        {
            using (ModsView.DeferRefresh())
            {
                ModsView.SortDescriptions.Clear();
                ModsView.SortDescriptions.Add(new SortDescription(CurrentSortProperty, CurrentSortDirection));
            }
            
            // Update the sort indicators after applying sort
            UpdateSortIndicators();
        }

        // Update the sort indicators for each column
        private void UpdateSortIndicators()
        {
            // Clear all indicators
            NameSortState = "";
            PackageIdSortState = "";
            SteamIdSortState = "";
            LocalUpdateDateSortState = "";

            // Set indicator for the sorted column
            string sortState = CurrentSortDirection == ListSortDirection.Ascending ? "SortedAscending" : "SortedDescending";
            
            switch (CurrentSortProperty)
            {
                case nameof(UpdateCheckItemViewModel.Name):
                    NameSortState = sortState;
                    break;
                case nameof(UpdateCheckItemViewModel.PackageId):
                    PackageIdSortState = sortState;
                    break;
                case nameof(UpdateCheckItemViewModel.SteamId):
                    SteamIdSortState = sortState;
                    break;
                case nameof(UpdateCheckItemViewModel.LocalUpdateDate):
                    LocalUpdateDateSortState = sortState;
                    break;
            }
        }

        // Method to get the selected ModItems still uses the original collection
        public IEnumerable<ModItem> GetSelectedMods()
        {
            return _modsToCheck.Where(vm => vm.IsSelected).Select(vm => vm.Mod);
        }
    }
}