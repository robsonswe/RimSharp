using RimSharp.Core.Commands;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public class DuplicateModDialogViewModel : DialogViewModelBase<bool>
    {
        public ObservableCollection<DuplicateModGroupViewModel> DuplicateGroups { get; }
            = new ObservableCollection<DuplicateModGroupViewModel>();

        public ICommand ApplyResolutionsCommand { get; }
        public ICommand CancelCommand { get; }

        public string GroupName { get; }

        private readonly Action<List<string>> _applyCallback;
        private readonly Action _cancelCallback;

        public DuplicateModDialogViewModel(
            List<IGrouping<string, ModItem>> duplicateGroups,
            Action<List<string>> applyCallback,
            Action cancelCallback)
            : base("Resolve Duplicate Mods")
        {
            _applyCallback = applyCallback ?? (list => { });
            _cancelCallback = cancelCallback ?? (() => { });

            if (duplicateGroups == null)
            {
                CloseDialog(false);
                return;
            }

            try
            {
                foreach (var group in duplicateGroups)
                {
                    if (group?.Count() > 1)
                    {
                        DuplicateGroups.Add(new DuplicateModGroupViewModel(group));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing duplicate groups: {ex}");
                CloseDialog(false);
            }

            ApplyResolutionsCommand = CreateCommand(ApplyResolutions);
            CancelCommand = CreateCommand(Cancel);
        }


        private void ApplyResolutions()
        {
            try
            {
                var pathsToDelete = new List<string>();
                foreach (var group in DuplicateGroups)
                {
                    if (group == null || group.IsIgnored)
                        continue;
                    var selectedMod = group.GetSelectedModToKeep();
                    if (selectedMod == null)
                        continue;
                    var pathsFromGroup = group.Mods?
                        .Where(m => m?.Original != null && m.Original != selectedMod && !string.IsNullOrEmpty(m.Path))
                        .Select(m => m.Path)
                        .ToList();
                    if (pathsFromGroup != null)
                    {
                        pathsToDelete.AddRange(pathsFromGroup);
                    }
                }

                var finalPathsToDelete = pathsToDelete?.Where(p => p != null).ToList() ?? new List<string>();

                Debug.WriteLine($"[ApplyResolutions] Preparing to call callback. Calculated {finalPathsToDelete.Count} paths for deletion:");
                if (finalPathsToDelete.Count > 0)
                {
                    for (int i = 0; i < finalPathsToDelete.Count; i++)
                    {
                        Debug.WriteLine($"  Callback Path [{i}]: '{finalPathsToDelete[i] ?? "NULL"}'");
                    }
                }
                if (finalPathsToDelete.Any(p => p == null))
                {
                    Debug.WriteLine("[ApplyResolutions] !!! WARNING: finalPathsToDelete contains NULL before callback !!!");
                }

                if (_applyCallback == null)
                {
                    Debug.WriteLine("[ApplyResolutions] ERROR: ApplyCallback is null.");
                    MessageBox.Show("Internal Error: Apply action is not configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    CloseDialog(false);
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        Debug.WriteLine($"[ApplyResolutions] Invoking callback (Line ~127) with {finalPathsToDelete.Count} paths.");
                        _applyCallback(finalPathsToDelete);
                        CloseDialog(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ApplyResolutions] !!! ERROR during callback execution: {ex}");
                        MessageBox.Show($"Failed to apply duplicate mod resolutions during callback execution: {ex.Message}",
                            "Callback Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        CloseDialog(false);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ApplyResolutions] Error preparing resolutions: {ex}");
                MessageBox.Show($"An unexpected error occurred while preparing to resolve duplicates: {ex.Message}",
                    "Preparation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CloseDialog(false);
            }
        }

        private void Cancel()
        {
            Application.Current.Dispatcher.Invoke(() =>
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