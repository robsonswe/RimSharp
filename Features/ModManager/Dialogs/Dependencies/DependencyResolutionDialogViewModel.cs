using RimSharp.AppDir.Dialogs;
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
        private ObservableCollection<MissingDependencyItemViewModel>? _missingDependencies = new();
        public ObservableCollection<MissingDependencyItemViewModel>? MissingDependencies
        {
            get => _missingDependencies;
            set
            {
                if (_missingDependencies != null)
                {
                    foreach (var item in _missingDependencies)
                    {
                        if (item != null)
                            item.PropertyChanged -= DependencyItem_PropertyChanged;
                    }
                }

                if (SetProperty(ref _missingDependencies, value))
                {
                    if (_missingDependencies != null)
                    {
                        foreach (var item in _missingDependencies)
                        {
                            if(item != null && item.IsSelectable)
                                item.PropertyChanged += DependencyItem_PropertyChanged;
                        }
                        UpdateSelectedCount();
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

        public DependencyResolutionDialogViewModel(
            IEnumerable<(string displayName, string packageId, string steamUrl, List<string> requiredBy)> missingDepsData)
            : base("Missing Dependencies")
        {
            var viewModels = missingDepsData
                .Select(dep => new MissingDependencyItemViewModel(
                    dep.displayName, dep.packageId, dep.steamUrl, dep.requiredBy))
                .OrderBy(vm => vm.DisplayName)
                .ToList();
            SelectAllCommand = CreateCommand(SelectAll, CanSelectAll);
            SelectNoneCommand = CreateCommand(SelectNone, CanSelectNone);

            MissingDependencies = new ObservableCollection<MissingDependencyItemViewModel>(viewModels);
        }

        private void DependencyItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MissingDependencyItemViewModel.IsSelected))
            {
                UpdateSelectedCount();
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
                item.IsSelected = true;
            }
        }

        private bool CanSelectNone() => MissingDependencies?.Any(item => item.IsSelected) ?? false;
        private void SelectNone()
        {
            if (MissingDependencies == null) return;
            foreach (var item in MissingDependencies.Where(i => i.IsSelected))
            {
                item.IsSelected = false;
            }
        }

        public List<string> GetSelectedSteamIds()
        {
            return MissingDependencies?
                .Where(item => item.IsSelected && item.IsSelectable && item.SteamId != null)
                .Select(item => item.SteamId!)
                .ToList() ?? new List<string>();
        }
    }
}
