#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.Dialogs.Strip
{

    public enum StrippableItemType { Folder, File }

    public class StrippableItemViewModel : ViewModelBase
    {
        private bool _isSelected;
        public StrippableModViewModel Parent { get; }
        public string Name { get; }
        public string RelativePath { get; }
        public string FullPath { get; }
        public long Size { get; } // In bytes
        public StrippableItemType ItemType { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    Parent.UpdateParentSelectionState();
                }
            }
        }

        public StrippableItemViewModel(StrippableModViewModel parent, string name, string relativePath, string fullPath, long size, StrippableItemType type)
        {
            Parent = parent;
            Name = name;
            RelativePath = relativePath;
            FullPath = fullPath;
            Size = size;
            ItemType = type;
            _isSelected = true; // Default to selected
        }
    }

    public class StrippableModViewModel : ViewModelBase
    {
        private bool? _isSelected = true; // Nullable for indeterminate state
        private bool _isExpanded;
        private bool _isUpdatingFromChildren;

        public ModItem Mod { get; }
        public ObservableCollection<StrippableItemViewModel> Children { get; }
        public long TotalStrippableSize => Children.Sum(c => c.Size);

        public bool? IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;

                if (_isUpdatingFromChildren)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    return;
                }

                bool targetValue = value ?? false;

                if (SetProperty(ref _isSelected, targetValue))
                {
                    // Propagate change to children
                    foreach (var child in Children)
                    {
                        child.IsSelected = targetValue;
                    }
                    OnPropertyChanged(nameof(SelectedSize));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ICommand ToggleExpandCommand { get; }

        public void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }

        public long SelectedSize => Children.Where(c => c.IsSelected).Sum(c => c.Size);

        public StrippableModViewModel(ModItem mod)
        {
            Mod = mod;
            Children = new ObservableCollection<StrippableItemViewModel>();
            ToggleExpandCommand = ReactiveUI.ReactiveCommand.Create(ToggleExpand);
        }

        // Called by a child when its selection changes
        public void UpdateParentSelectionState()
        {
            _isUpdatingFromChildren = true;
            try
            {
                var selectedCount = Children.Count(c => c.IsSelected);
                if (selectedCount == 0)
                {
                    IsSelected = false;
                }
                else if (selectedCount == Children.Count)
                {
                    IsSelected = true;
                }
                else
                {
                    IsSelected = null; // Indeterminate state
                }
            }
            finally
            {
                _isUpdatingFromChildren = false;
            }
            OnPropertyChanged(nameof(SelectedSize));
        }
    }

    public class StripDialogViewModel : DialogViewModelBase<(bool, IEnumerable<string>?)>
    {
        public ObservableCollection<StrippableModViewModel> StrippableMods { get; }
        public ICommand StripCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand OpenInExplorerCommand { get; }

        public long TotalSelectedSize => StrippableMods.Sum(m => m.SelectedSize);

        public StripDialogViewModel(IEnumerable<StrippableModViewModel> mods) : base("Mods Stripping Dialog")
        {
            StrippableMods = new ObservableCollection<StrippableModViewModel>(mods);

            foreach (var mod in StrippableMods)
            {
                mod.PropertyChanged += (s, e) => 
                { 
                    if (e.PropertyName == nameof(StrippableModViewModel.SelectedSize))
                    {
                        OnPropertyChanged(nameof(TotalSelectedSize));
                    }
                };
            }

            CancelCommand = CreateCommand(() => CloseDialog((false, null)));
            StripCommand = CreateCommand(ExecuteStrip);
            SelectAllCommand = CreateCommand(() => SetAllSelection(true));
            SelectNoneCommand = CreateCommand(() => SetAllSelection(false));
            OpenInExplorerCommand = CreateCommand<string>(ExecuteOpenInExplorer);
        }

        private void ExecuteStrip()
        {
            var pathsToDelete = StrippableMods.SelectMany(m => m.Children)
                                              .Where(c => c.IsSelected)
                                              .Select(c => c.FullPath);
            CloseDialog((true, pathsToDelete));
        }

        private void SetAllSelection(bool isSelected)
        {
            foreach (var mod in StrippableMods)
            {
                mod.IsSelected = isSelected;
            }
        }

        private void ExecuteOpenInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", $"\"{path}\"");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StripDialogViewModel] Failed to open path in explorer: {ex.Message}");

            }
        }
    }
}


