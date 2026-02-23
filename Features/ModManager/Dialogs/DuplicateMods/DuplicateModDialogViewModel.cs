using RimSharp.Core.Commands;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public class DuplicateModDialogViewModel : DialogViewModelBase<bool>
    {
        public ObservableCollection<DuplicateModGroupViewModel> DuplicateGroups { get; }
            = new ObservableCollection<DuplicateModGroupViewModel>();

        public ICommand ApplyResolutionsCommand { get; }
        public ICommand CancelCommand { get; }

        private readonly Func<List<string>, Task> _applyCallback;
        private readonly Action _cancelCallback;

        public DuplicateModDialogViewModel(
            List<IGrouping<string, ModItem>> duplicateGroups,
            Func<List<string>, Task> applyCallback,
            Action cancelCallback)
            : base("Resolve Duplicate Mods")
        {
            _applyCallback = applyCallback ?? (list => Task.CompletedTask);
            _cancelCallback = cancelCallback ?? (() => { });

            ApplyResolutionsCommand = CreateCommand(ApplyResolutions);
            CancelCommand = CreateCommand(Cancel);

            if (duplicateGroups == null)
            {
                CloseDialog(false);
                return;
            }

            foreach (var group in duplicateGroups)
            {
                DuplicateGroups.Add(new DuplicateModGroupViewModel(group));
            }
        }

        private async void ApplyResolutions()
        {
            try
            {
                var pathsToDelete = new List<string>();
                foreach (var group in DuplicateGroups)
                {
                    if (group == null || group.IsIgnored) continue;
                    var selectedMod = group.GetSelectedModToKeep();
                    if (selectedMod == null) continue;
                    var pathsFromGroup = group.Mods?
                        .Where(m => m?.Original != null && m.Original != selectedMod && !string.IsNullOrEmpty(m.Path))
                        .Select(m => m.Path!)
                        .ToList();
                    if (pathsFromGroup != null) pathsToDelete.AddRange(pathsFromGroup);
                }

                var finalPathsToDelete = pathsToDelete.Where(p => p != null).ToList();

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await _applyCallback(finalPathsToDelete);
                        CloseDialog(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error during callback execution: {ex}");
                        CloseDialog(false);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preparing resolutions: {ex}");
                CloseDialog(false);
            }
        }

        private void Cancel()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _cancelCallback?.Invoke();
                    CloseDialog(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in Cancel: {ex}");
                    CloseDialog(false);
                }
            });
        }
    }
}
