#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs; // For DialogViewModelBase<T>

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    // Define the result type
    public enum MissingModSelectionResult
    {
        Cancel,
        Download
    }

    // Define what the dialog returns: The result enum and the list of selected Steam IDs
    public class MissingModSelectionDialogOutput
    {
        public MissingModSelectionResult Result { get; set; } = MissingModSelectionResult.Cancel;
        public List<string> SelectedSteamIds { get; set; } = new List<string>();
    }

    public class MissingModSelectionDialogViewModel : DialogViewModelBase<MissingModSelectionDialogOutput>
    {
        private ObservableCollection<MissingModGroupViewModel> _modGroups = new();
        private ObservableCollection<string> _unknownModIds = new();

        public ObservableCollection<MissingModGroupViewModel> ModGroups
        {
            get => _modGroups;
            set => SetProperty(ref _modGroups, value);
        }

        public ObservableCollection<string> UnknownModIds
        {
            get => _unknownModIds;
            set => SetProperty(ref _unknownModIds, value);
        }

        public bool HasUnknownMods => UnknownModIds.Any();
        public bool HasKnownMods => ModGroups.Any();


        public ICommand DownloadCommand { get; }
        public ICommand CancelCommand { get; }


        public MissingModSelectionDialogViewModel(
            IEnumerable<MissingModGroupViewModel> groups,
            IEnumerable<string> unknownIds)
            : base("Select Missing Mods for Download") // Dialog Title
        {
            ModGroups = new ObservableCollection<MissingModGroupViewModel>(groups);
            UnknownModIds = new ObservableCollection<string>(unknownIds);

            // Observe changes in SelectedVariant for each group to update CanExecute
            foreach (var group in ModGroups)
            {
                group.PropertyChanged += Group_PropertyChanged;
            }

            DownloadCommand = CreateCommand(
                ExecuteDownload,
                CanExecuteDownload); // No need to list properties here, handled by event subscription

            CancelCommand = CreateCommand(ExecuteCancel);

            // Initial CanExecute check
            (DownloadCommand as Core.Commands.Base.IDelegateCommand)?.RaiseCanExecuteChanged();
        }

        private void Group_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MissingModGroupViewModel.SelectedVariant))
            {
                 // Re-evaluate CanExecute when a selection changes
                (DownloadCommand as Core.Commands.Base.IDelegateCommand)?.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteDownload()
        {
            // Can download only if ALL groups have a variant selected
             // And there must be at least one group to select from
            return ModGroups.Any() && ModGroups.All(g => g.SelectedVariant != null);
        }

        private void ExecuteDownload()
        {
            var selectedSteamIds = ModGroups
                .Where(g => g.SelectedVariant != null)
                .Select(g => g.SelectedVariant!.SteamId) // Safe due to CanExecute check
                .Where(id => !string.IsNullOrEmpty(id)) // Extra safety
                .Distinct() // Ensure unique IDs
                .ToList();

            var output = new MissingModSelectionDialogOutput
            {
                Result = MissingModSelectionResult.Download,
                SelectedSteamIds = selectedSteamIds
            };
            CloseDialog(output);
        }

        private void ExecuteCancel()
        {
            var output = new MissingModSelectionDialogOutput { Result = MissingModSelectionResult.Cancel };
            CloseDialog(output);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                     // Unsubscribe from group property changes
                    foreach (var group in ModGroups)
                    {
                        group.PropertyChanged -= Group_PropertyChanged;
                    }
                }
                base.Dispose(disposing);
            }
        }

        // Override the mapping for the Window's DialogResult
        protected override void MapResultToWindowResult(MissingModSelectionDialogOutput result)
        {
            DialogResultForWindow = result?.Result == MissingModSelectionResult.Download;
        }
    }
}
