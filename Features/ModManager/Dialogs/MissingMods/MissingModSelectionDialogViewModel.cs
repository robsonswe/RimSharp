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
                CanExecuteDownload); // CanExecute logic changed below

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
            // 1. Filter out groups that ONLY contain unpublished mods (these don't require selection).
            var groupsRequiringSelection = ModGroups
                .Where(g => g.Variants.Any(v => v.IsPublished)) // Find groups that HAVE at least one published variant
                .ToList();

            // 2. If there are no groups that require a selection (e.g., all mods were unpublished or the list was empty),
            //    then we cannot download anything.
            if (!groupsRequiringSelection.Any())
            {
                 // Optional: Add debug logging if needed
                 // System.Diagnostics.Debug.WriteLine("[CanExecuteDownload] Result: false (No groups require selection or have published variants)");
                return false;
            }

            // 3. Check if ALL groups that *require* a selection actually *have* a selection made.
            //    We already know the selection MUST be a published one if IsSelectable works correctly,
            //    but checking SelectedVariant != null is sufficient here because only published ones are selectable.
            bool allRequiredSelectionsMade = groupsRequiringSelection
                .All(g => g.SelectedVariant != null);

            // Optional: Add debug logging if needed
            // System.Diagnostics.Debug.WriteLine($"[CanExecuteDownload] {groupsRequiringSelection.Count} groups require selection. All selections made = {allRequiredSelectionsMade}");

            return allRequiredSelectionsMade;
        }

        private void ExecuteDownload()
        {
            var selectedSteamIds = ModGroups
                // Filter groups: Ensure a variant is selected AND it's published
                .Where(g => g.SelectedVariant != null && g.SelectedVariant.IsPublished)
                 // Select the SteamId (safe due to Where clause)
                .Select(g => g.SelectedVariant!.SteamId)
                 // Extra safety: Ensure ID isn't empty (shouldn't happen)
                .Where(id => !string.IsNullOrEmpty(id))
                 // Ensure unique IDs
                .Distinct()
                .ToList();

            // Defensive check: Although CanExecuteDownload should now allow this only
            // if at least one valid selection exists, we double-check.
            if (!selectedSteamIds.Any())
            {
                 // This case might occur if CanExecuteDownload logic changes or has edge cases.
                 System.Diagnostics.Debug.WriteLine("WARNING: ExecuteDownload called but no valid published mods selected. This might indicate an issue in CanExecuteDownload logic.");
                 // Optionally, show a message to the user or just return.
                 // For now, we just return, preventing the dialog close with Download result.
                 return;
                 // OR: If you decide even empty selection is ok (e.g., user cleared selections after enabling),
                 //     you might proceed, but the current Output structure expects IDs.
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
             // DialogResult = true only if the user explicitly chose Download AND there were IDs selected.
             // The check in ExecuteDownload should prevent closing with Download result if selectedSteamIds is empty.
            DialogResultForWindow = result?.Result == MissingModSelectionResult.Download && (result?.SelectedSteamIds.Any() ?? false);
        }
    }
}
