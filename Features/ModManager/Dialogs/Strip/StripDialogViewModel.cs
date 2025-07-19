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
    // --- Helper ViewModels for the dialog ---

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

        public ModItem Mod { get; }
        public ObservableCollection<StrippableItemViewModel> Children { get; }
        public long TotalStrippableSize => Children.Sum(c => c.Size);

        public bool? IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    if (value.HasValue) // Not indeterminate
                    {
                        // Propagate change to children
                        foreach (var child in Children)
                        {
                            child.IsSelected = value.Value;
                        }
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

        public long SelectedSize => Children.Where(c => c.IsSelected).Sum(c => c.Size);

        public StrippableModViewModel(ModItem mod)
        {
            Mod = mod;
            Children = new ObservableCollection<StrippableItemViewModel>();
        }

        // Called by a child when its selection changes
        public void UpdateParentSelectionState()
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
            OnPropertyChanged(nameof(SelectedSize));
        }
    }

    // --- Main Dialog ViewModel ---
    
    // The result is a tuple: (bool indicating if Strip was clicked, list of paths to delete)
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
            // Listen for property changes on each mod to update the total size
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
                    // For a file, open the containing folder and select the file
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else if (Directory.Exists(path))
                {
                    // For a directory, just open it
                    Process.Start("explorer.exe", $"\"{path}\"");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StripDialogViewModel] Failed to open path in explorer: {ex.Message}");
                // Optionally show a dialog service error message to the user here
            }
        }
    }
}