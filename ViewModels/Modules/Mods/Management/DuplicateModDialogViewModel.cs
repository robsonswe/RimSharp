using RimSharp.Handlers;
using RimSharp.Models;
using RimSharp.ViewModels.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace RimSharp.ViewModels.Modules.Mods.Management
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

            ApplyResolutionsCommand = new RelayCommand(_ => ApplyResolutions());
            CancelCommand = new RelayCommand(_ => Cancel());
        }

        private void ApplyResolutions()
        {
            try
            {
                var pathsToDelete = new List<string>();
                // ... (logic to populate pathsToDelete based on selections, same as before) ...
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


                // --- Final Safety Check & Debugging ---
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
                // --- End Debugging ---


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
                        _applyCallback(finalPathsToDelete); // Line 127 (approx)
                        CloseDialog(true);
                    }
                    catch (Exception ex)
                    {
                        // Error is caught here from the callback execution!
                        Debug.WriteLine($"[ApplyResolutions] !!! ERROR during callback execution: {ex}");
                        MessageBox.Show($"Failed to apply duplicate mod resolutions during callback execution: {ex.Message}", // Typo fixed: reference, instance
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

    public class DuplicateModGroupViewModel : ViewModelBase
    {
        public ObservableCollection<ModItemWrapper> Mods { get; } = new ObservableCollection<ModItemWrapper>();
        public string PackageId { get; }
        public string GroupName { get; }


        private bool _isIgnored;
        public bool IsIgnored
        {
            get => _isIgnored;
            set
            {
                if (SetProperty(ref _isIgnored, value))
                {
                    if (value)
                    {
                        foreach (var mod in Mods)
                        {
                            mod.IsActive = false;
                        }
                    }
                }
            }
        }

        public DuplicateModGroupViewModel(IGrouping<string, ModItem> group)
        {
            PackageId = group.Key;
            GroupName = $"DuplicateGroup_{group.GetHashCode()}";
            _isIgnored = false;

            foreach (var mod in group)
            {
                if (mod != null)
                {
                    var wrapper = new ModItemWrapper(mod, this);
                    Mods.Add(wrapper);
                }
            }

            var defaultMod = group.OrderByDescending(m => m?.IsActive ?? false)
                  .ThenByDescending(m => m?.SupportedVersions?.FirstOrDefault())
                  .FirstOrDefault();

            if (defaultMod != null)
            {
                var wrapper = Mods.FirstOrDefault(w => w?.Original == defaultMod);
                if (wrapper != null)
                {
                    wrapper.IsActive = true;
                }
            }
        }

        public ModItem GetSelectedModToKeep()
        {
            return Mods.FirstOrDefault(m => m?.IsActive == true)?.Original;
        }

        public void UpdateSelection(ModItemWrapper selected)
        {
            IsIgnored = false;

            foreach (var mod in Mods)
            {
                if (mod != null && mod != selected)
                {
                    mod.IsActive = false;
                }
            }
        }
    }

    public class ModItemWrapper : ViewModelBase
    {
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    if (value)
                    {
                        ParentGroup?.UpdateSelection(this);
                        Original.IsActive = true;
                    }
                    else
                    {
                        Original.IsActive = false;
                    }
                }
            }
        }

        public ModItem Original { get; }
        public DuplicateModGroupViewModel ParentGroup { get; }

        public string Name => Original?.Name;
        public string Authors => Original?.Authors;
        public string ModVersion => Original?.ModVersion;
        public string Path => Original?.Path;
        public string SteamId => Original?.SteamId;
        public string Url => Original?.Url;
        public string SteamUrl => Original?.SteamUrl;
        public string ExternalUrl => Original?.ExternalUrl;

        public string SupportedVersions => Original?.SupportedVersions != null && Original.SupportedVersions.Any()
    ? string.Join(", ", Original.SupportedVersions)
    : "Unknown";


        public ModItemWrapper(ModItem original, DuplicateModGroupViewModel parentGroup)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));
            ParentGroup = parentGroup ?? throw new ArgumentNullException(nameof(parentGroup));
            _isActive = original.IsActive;
        }
    }
}