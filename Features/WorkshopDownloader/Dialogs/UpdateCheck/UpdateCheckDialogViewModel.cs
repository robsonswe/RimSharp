using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using ReactiveUI;

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
        public IEnumerable<UpdateCheckItemViewModel> ModsView => _modsToCheck;

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SelectActiveCommand { get; }
        public ICommand SelectByTimeframeCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CancelCommand { get; }

        public int TotalModCount => _modsToCheck.Count;
        private int _selectedModCount;
        public int SelectedModCount
        {
            get => _selectedModCount;
            private set => this.RaiseAndSetIfChanged(ref _selectedModCount, value);
        }

        public bool SelectAll
        {
            get => _modsToCheck.Any() && _modsToCheck.All(m => m.IsSelected);
            set => SetSelectionAll(value);
        }

        public UpdateCheckDialogViewModel(IEnumerable<ModItem> mods)
            : base("Workshop Update Check")
        {
            _modsToCheck = new ObservableCollection<UpdateCheckItemViewModel>(
                mods.Select(m => new UpdateCheckItemViewModel(m))
            );

            foreach (var item in _modsToCheck)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }

            UpdateSelectedCount();

            SelectAllCommand = CreateCommand(() => SetSelectionAll(true));
            SelectNoneCommand = CreateCommand(() => SetSelectionAll(false));
            SelectActiveCommand = CreateCommand(SelectActiveOnly);
            SelectByTimeframeCommand = CreateCommand<string>(SelectByTimeframe);
            UpdateCommand = CreateCommand(ExecuteUpdate, () => SelectedModCount > 0, nameof(SelectedModCount));
            CancelCommand = CreateCommand(() => CloseDialog(UpdateCheckDialogResult.Cancel));
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UpdateCheckItemViewModel.IsSelected))
            {
                UpdateSelectedCount();
                this.RaisePropertyChanged(nameof(SelectAll));
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedModCount = _modsToCheck.Count(m => m.IsSelected);
        }

        private void SetSelectionAll(bool isSelected)
        {
            foreach (var item in _modsToCheck)
            {
                item.IsSelected = isSelected;
            }
        }

        private void SelectActiveOnly()
        {
            foreach (var item in _modsToCheck)
            {
                item.IsSelected = item.ModItem.IsActive;
            }
        }

        private void SelectByTimeframe(string timeframe)
        {
            DateTime cutoff = timeframe switch
            {
                "week" => DateTime.Now.AddDays(-7),
                "month" => DateTime.Now.AddMonths(-1),
                "6months" => DateTime.Now.AddMonths(-6),
                "year" => DateTime.Now.AddYears(-1),
                _ => DateTime.MinValue
            };

            foreach (var item in _modsToCheck)
            {
                if (timeframe == "outdated")
                {
                    item.IsSelected = item.ModItem.IsOutdatedRW;
                }
                else
                {
                    item.IsSelected = item.LocalUpdateDateTime < cutoff;
                }
            }
        }

        private void ExecuteUpdate()
        {
            CloseDialog(UpdateCheckDialogResult.CheckUpdates);
        }

        public IEnumerable<ModItem> GetSelectedMods()
        {
            return _modsToCheck
                .Where(m => m.IsSelected)
                .Select(m => m.ModItem);
        }

        public IEnumerable<string> GetSelectedSteamIds()
        {
            return _modsToCheck
                .Where(m => m.IsSelected && !string.IsNullOrEmpty(m.SteamId))
                .Select(m => m.SteamId!);
        }
    }

    public class UpdateCheckItemViewModel : ViewModelBase
    {
        public ModItem ModItem { get; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public string Name => ModItem.Name ?? "Unknown";
        public string PackageId => ModItem.PackageId ?? "Unknown";
        public string SteamId => ModItem.SteamId ?? "";
        public string LocalUpdateDate => ModItem.UpdateDate ?? "N/A";
        public DateTime LocalUpdateDateTime { get; }

        public UpdateCheckItemViewModel(ModItem modItem)
        {
            ModItem = modItem;
            _isSelected = modItem.IsActive;

            if (!string.IsNullOrWhiteSpace(modItem.UpdateDate) &&
                DateTime.TryParseExact(modItem.UpdateDate, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                LocalUpdateDateTime = parsedDate;
            }
            else
            {
                LocalUpdateDateTime = DateTime.MinValue;
            }
        }
    }
}
