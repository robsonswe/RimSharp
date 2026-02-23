using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Core.Commands;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
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
        public ObservableCollection<IncompatibilityGroupViewModel> IncompatibilityGroups { get; }
            = new ObservableCollection<IncompatibilityGroupViewModel>();

        private bool _isCancelEnabled = true;

        public bool IsCancelEnabled
        {
            get => _isCancelEnabled;
            set => this.RaiseAndSetIfChanged(ref _isCancelEnabled, value);
        }

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
                IncompatibilityGroups.Add(new IncompatibilityGroupViewModel(group));
            }

            ApplyResolutionsCommand = CreateCommand(ApplyResolutions);
            CancelCommand = CreateCommand(Cancel, () => IsCancelEnabled, nameof(IsCancelEnabled));
        }

        private void ApplyResolutions()
        {
            var modsToRemove = new List<ModItem>();

            foreach (var group in IncompatibilityGroups)
            {
                modsToRemove.AddRange(group.GetSelectedModsToRemove());
            }

            _applyCallback?.Invoke(modsToRemove.Distinct().ToList());
            CloseDialog(true);
        }

        private void Cancel()
        {
            _cancelCallback?.Invoke();
            CloseDialog(false);
        }
    }
}
