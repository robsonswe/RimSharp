#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Commands.Base;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public enum MissingModSelectionResult
    {
        Cancel,
        Download
    }

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

        /// <summary>
        /// Summary text for the section header, e.g. "2 of 3 selected".
        /// Only counts groups that have at least one published (selectable) variant.
        /// </summary>
        public string SelectionSummary
        {
            get
            {
                var total = ModGroups.Count(g => g.HasSelectableVariants);
                var selected = ModGroups.Count(g => g.HasSelectableVariants && g.SelectedVariant != null);
                return $"{selected} of {total} selected";
            }
        }

        /// <summary>
        /// Status bar text for the footer.
        /// </summary>
        public string StatusText
        {
            get
            {
                var parts = new List<string>();
                if (HasKnownMods)
                {
                    var selectableCount = ModGroups.Count(g => g.HasSelectableVariants);
                    var selectedCount = ModGroups.Count(g => g.HasSelectableVariants && g.SelectedVariant != null);
                    parts.Add($"{selectedCount}/{selectableCount} mods selected for download");
                }
                if (HasUnknownMods)
                {
                    parts.Add($"{UnknownModIds.Count} mod(s) not found in database");
                }
                return string.Join("  •  ", parts);
            }
        }

        /// <summary>
        /// Display label for the unknown mods section header count.
        /// </summary>
        public string UnknownModCount
        {
            get
            {
                var count = UnknownModIds.Count;
                return count == 1 ? "1 mod" : $"{count} mods";
            }
        }

        public ICommand DownloadCommand { get; }
        public ICommand CancelCommand { get; }

        public MissingModSelectionDialogViewModel(
            IEnumerable<MissingModGroupViewModel> groups,
            IEnumerable<string> unknownIds)
            : base("Select Missing Mods for Download")
        {
            ModGroups = new ObservableCollection<MissingModGroupViewModel>(groups);
            UnknownModIds = new ObservableCollection<string>(unknownIds);

            foreach (var group in ModGroups)
            {
                group.PropertyChanged += Group_PropertyChanged;
            }

            DownloadCommand = CreateCommand(ExecuteDownload, CanExecuteDownload);
            CancelCommand = CreateCommand(ExecuteCancel);

            (DownloadCommand as IDelegateCommand)?.RaiseCanExecuteChanged();
        }

        private void Group_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MissingModGroupViewModel.SelectedVariant))
            {
                (DownloadCommand as IDelegateCommand)?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectionSummary));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private bool CanExecuteDownload()
        {
            var groupsRequiringSelection = ModGroups
                .Where(g => g.Variants.Any(v => v.IsPublished))
                .ToList();

            if (!groupsRequiringSelection.Any())
            {
                return false;
            }

            return groupsRequiringSelection.All(g => g.SelectedVariant != null);
        }

        private void ExecuteDownload()
        {
            var selectedSteamIds = ModGroups
                .Where(g => g.SelectedVariant != null && g.SelectedVariant.IsPublished)
                .Select(g => g.SelectedVariant!.SteamId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            if (!selectedSteamIds.Any())
            {
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
                    foreach (var group in ModGroups)
                    {
                        group.PropertyChanged -= Group_PropertyChanged;
                    }
                }
                base.Dispose(disposing);
            }
        }

        protected override void MapResultToWindowResult(MissingModSelectionDialogOutput result)
        {
            DialogResultForWindow = result?.Result == MissingModSelectionResult.Download && (result?.SelectedSteamIds.Any() ?? false);
        }
    }
}
