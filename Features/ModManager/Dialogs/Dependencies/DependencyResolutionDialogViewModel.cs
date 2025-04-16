using RimSharp.MyApp.Dialogs; // For DialogViewModelBase
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.Dialogs.Dependencies
{
    public class DependencyResolutionDialogViewModel : DialogViewModelBase<DependencyResolutionDialogResult>
    {
        private ObservableCollection<MissingDependencyItemViewModel> _missingDependencies;
        public ObservableCollection<MissingDependencyItemViewModel> MissingDependencies
        {
            get => _missingDependencies;
            set
            {
                // Unsubscribe from old collection items if necessary
                if (_missingDependencies != null)
                {
                    foreach (var item in _missingDependencies)
                    {
                        item.PropertyChanged -= DependencyItem_PropertyChanged;
                    }
                }

                if (SetProperty(ref _missingDependencies, value))
                {
                    // Subscribe to new collection items
                    if (_missingDependencies != null)
                    {
                        foreach (var item in _missingDependencies)
                        {
                            // Only subscribe if the item can actually change selection state
                            if(item.IsSelectable)
                                item.PropertyChanged += DependencyItem_PropertyChanged;
                        }
                        UpdateSelectedCount(); // Initial count
                    }
                }
            }
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value);
        }

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }

        // Constructor takes the raw list from ModListManager
        public DependencyResolutionDialogViewModel(
            IEnumerable<(string displayName, string packageId, string steamUrl, List<string> requiredBy)> missingDepsData)
            : base("Missing Dependencies") // Set Dialog Title
        {
            var viewModels = missingDepsData
                .Select(dep => new MissingDependencyItemViewModel(
                    dep.displayName, dep.packageId, dep.steamUrl, dep.requiredBy))
                .OrderBy(vm => vm.DisplayName) // Optionally sort
                .ToList();

            // Initialize commands using base class helpers
            SelectAllCommand = CreateCommand(SelectAll, CanSelectAll);
            SelectNoneCommand = CreateCommand(SelectNone, CanSelectNone);

            // Assign to the property to trigger subscription logic
            MissingDependencies = new ObservableCollection<MissingDependencyItemViewModel>(viewModels);
            // Initial count update happens in the setter

            // Observe the collection itself for changes that might affect SelectAll/None CanExecute
             // (Though adding/removing items isn't expected in this dialog)
            // If needed: MissingDependencies.CollectionChanged += (s, e) => RaiseCanExecuteChangedForAllCommands();
        }

        private void DependencyItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MissingDependencyItemViewModel.IsSelected))
            {
                UpdateSelectedCount();
                // Re-evaluate CanExecute for SelectAll/None
                (SelectAllCommand as Core.Commands.Base.DelegateCommand)?.RaiseCanExecuteChanged();
                (SelectNoneCommand as Core.Commands.Base.DelegateCommand)?.RaiseCanExecuteChanged();
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedCount = MissingDependencies?.Count(item => item.IsSelected) ?? 0;
        }

        private bool CanSelectAll() => MissingDependencies?.Any(item => item.IsSelectable && !item.IsSelected) ?? false;
        private void SelectAll()
        {
            if (MissingDependencies == null) return;
            foreach (var item in MissingDependencies.Where(i => i.IsSelectable))
            {
                item.IsSelected = true; // Setter triggers PropertyChanged -> UpdateSelectedCount
            }
        }

        private bool CanSelectNone() => MissingDependencies?.Any(item => item.IsSelected) ?? false;
        private void SelectNone()
        {
            if (MissingDependencies == null) return;
            foreach (var item in MissingDependencies.Where(i => i.IsSelected)) // Only iterate selectable ones
            {
                item.IsSelected = false; // Setter triggers PropertyChanged -> UpdateSelectedCount
            }
        }

        /// <summary>
        /// Gets the Steam IDs of the currently selected, downloadable dependencies.
        /// </summary>
        public List<string> GetSelectedSteamIds()
        {
            return MissingDependencies?
                .Where(item => item.IsSelected && item.IsSelectable) // Ensure it's selected AND selectable (has ID)
                .Select(item => item.SteamId)
                .ToList() ?? new List<string>();
        }

         // Helper method to raise CanExecuteChanged for relevant commands
         // Might not be strictly necessary if observing properties correctly
        // private void RaiseCanExecuteChangedForAllCommands()
        // {
        //     (SelectAllCommand as Core.Commands.Base.DelegateCommand)?.RaiseCanExecuteChanged();
        //     (SelectNoneCommand as Core.Commands.Base.DelegateCommand)?.RaiseCanExecuteChanged();
        //     // Add other commands if needed
        // }
    }
}
