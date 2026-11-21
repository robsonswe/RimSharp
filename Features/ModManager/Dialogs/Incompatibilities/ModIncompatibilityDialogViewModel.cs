using RimSharp.AppDir.Dialogs;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ReactiveUI;

namespace RimSharp.Features.ModManager.Dialogs.Incompatibilities
{
    public class ModIncompatibilityDialogViewModel : DialogViewModelBase<bool>
    {
        public ObservableCollection<IncompatibilityGroupViewModel> IncompatibilityGroups { get; } = new();

        private bool _isCancelEnabled = true;
        public bool IsCancelEnabled
        {
            get => _isCancelEnabled;
            set => this.RaiseAndSetIfChanged(ref _isCancelEnabled, value);
        }

        public int HardGroupCount => IncompatibilityGroups.Count(g => g.ContainsHardIncompatibility);
        public int SoftGroupCount => IncompatibilityGroups.Count - HardGroupCount;
        public bool HasHardGroups => HardGroupCount > 0;
        public bool HasSoftGroups => SoftGroupCount > 0;
        public string HardBadgeText => $"{HardGroupCount} Hard";
        public string SoftBadgeText => $"{SoftGroupCount} Soft";

        public int TotalDeactivationsCount =>
            IncompatibilityGroups.SelectMany(g => g.GetSelectedModsToRemove()).Distinct().Count();

        public string FooterSummaryText => TotalDeactivationsCount switch
        {
            0 => "No mods will be deactivated",
            1 => "1 mod will be deactivated from your active list",
            _ => $"{TotalDeactivationsCount} mods will be deactivated from your active list"
        };

        public ICommand ApplyResolutionsCommand { get; }
        public ICommand CancelCommand { get; }

        private readonly List<IncompatibilityGroup> _originalGroups;
        private readonly Action<List<ModItem>> _applyCallback;
        private readonly Action _cancelCallback;

        public ModIncompatibilityDialogViewModel(
            List<IncompatibilityGroup> groups,
            Action<List<ModItem>> applyCallback,
            Action cancelCallback,
            bool canCancel = true)
            : base("Resolve Mod Incompatibilities")
        {
            _originalGroups = groups;
            _applyCallback = applyCallback;
            _cancelCallback = cancelCallback;
            _isCancelEnabled = canCancel;

            foreach (var group in groups)
            {
                var groupVm = new IncompatibilityGroupViewModel(group);
                groupVm.SelectionChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(TotalDeactivationsCount));
                    OnPropertyChanged(nameof(FooterSummaryText));
                };
                IncompatibilityGroups.Add(groupVm);
            }

            ApplyResolutionsCommand = CreateCommand(ApplyResolutions);
            CancelCommand = CreateCommand(Cancel, () => IsCancelEnabled, nameof(IsCancelEnabled));
        }

        private void ApplyResolutions()
        {
            var modsToRemove = IncompatibilityGroups
                .SelectMany(g => g.GetSelectedModsToRemove())
                .Distinct()
                .ToList();

            _applyCallback?.Invoke(modsToRemove);
            CloseDialog(true);
        }

        private void Cancel()
        {
            _cancelCallback?.Invoke();
            CloseDialog(false);
        }
    }
}