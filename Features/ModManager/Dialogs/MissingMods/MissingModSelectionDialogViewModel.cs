#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs; // For DialogViewModelBase<T>
using RimSharp.Core.Commands.Base; // For IDelegateCommand

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
            (DownloadCommand as IDelegateCommand)?.RaiseCanExecuteChanged();
        }

        private void Group_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MissingModGroupViewModel.SelectedVariant))
            {
                 // Re-evaluate CanExecute when a selection changes
                (DownloadCommand as IDelegateCommand)?.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteDownload()
        {
            // Can download only if:
            // 1. There is at least one group to select from.
            // 2. ALL groups have a variant selected.
            // 3. The selected variant in ALL groups is published (IsPublished == true).
            return ModGroups.Any() && ModGroups.All(g => g.SelectedVariant != null && g.SelectedVariant.IsPublished);
        }

        private void ExecuteDownload()
        {
            var selectedSteamIds = ModGroups
                // Ensure we only take selections that are valid according to CanExecute
                .Where(g => g.SelectedVariant != null && g.SelectedVariant.IsPublished)
                .Select(g => g.SelectedVariant!.SteamId) // Safe due to Where clause
                .Where(id => !string.IsNullOrEmpty(id)) // Extra safety
                .Distinct() // Ensure unique IDs
                .ToList();

            // Double-check if any IDs were actually collected (although CanExecute should prevent this being empty)
            if (!selectedSteamIds.Any())
            {
                 // This case should ideally not happen if CanExecuteDownload is correct,
                 // but handle defensively. Maybe log a warning.
                 System.Diagnostics.Debug.WriteLine("WARNING: ExecuteDownload called but no valid published mods selected.");
                 return;
            }

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
