#nullable enable
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.ObservableCollections;
using RimSharp.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;

namespace RimSharp.Features.ModManager.Dialogs.Strip
{
    public enum StrippableItemType { Folder, File }

    public enum StripSortMode
    {
        LargestFirst,
        NameAZ
    }

    public class StrippableItemViewModel : ViewModelBase
    {
        private bool _isSelected;
        private bool _isLastChild;

        public StrippableModViewModel Parent { get; }
        public string Name { get; }
        public string RelativePath { get; }
        public string FullPath { get; }
        public long Size { get; }
        public StrippableItemType ItemType { get; }

        public bool IsFolder => ItemType == StrippableItemType.Folder;
        public bool IsFile => ItemType == StrippableItemType.File;
        public string ItemTypeLabel => IsFolder ? "Folder" : "File";
        public bool HasDistinctRelativePath =>
            !string.IsNullOrWhiteSpace(RelativePath) &&
            !string.Equals(RelativePath, Name, StringComparison.OrdinalIgnoreCase);

        public bool IsLastChild
        {
            get => _isLastChild;
            internal set => SetProperty(ref _isLastChild, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value) && !Parent.IsBulkUpdating)
                {
                    Parent.UpdateParentSelectionState();
                }
            }
        }

        public StrippableItemViewModel(
            StrippableModViewModel parent,
            string name,
            string relativePath,
            string fullPath,
            long size,
            StrippableItemType type)
        {
            Parent = parent;
            Name = name;
            RelativePath = relativePath;
            FullPath = fullPath;
            Size = size;
            ItemType = type;
            _isSelected = true;
        }
    }

    public class StrippableModViewModel : ViewModelBase
    {
        private bool? _isSelected = true;
        private bool _isExpanded;
        private bool _isUpdatingFromChildren;
        private double _sizeSharePercent = 100.0;
        private long _selectedSize;
        private long _totalStrippableSize;

        internal bool IsBulkUpdating { get; private set; }

        public ModItem Mod { get; }
        public ObservableCollection<StrippableItemViewModel> Children { get; }
        public long TotalStrippableSize => _totalStrippableSize;
        public string ItemsCountBadge => Children.Count == 1 ? "1 item" : $"{Children.Count} items";
        public bool IsExpandedWithChildren => IsExpanded && Children.Count > 0;

        public double SizeSharePercent
        {
            get => _sizeSharePercent;
            internal set => SetProperty(ref _sizeSharePercent, value);
        }

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
                    IsBulkUpdating = true;
                    try
                    {
                        foreach (var child in Children)
                            child.IsSelected = targetValue;
                    }
                    finally
                    {
                        IsBulkUpdating = false;
                    }

                    RecalculateSelectedSize();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                    OnPropertyChanged(nameof(IsExpandedWithChildren));
            }
        }

        public ICommand ToggleExpandCommand { get; }

        public void ToggleExpand() => IsExpanded = !IsExpanded;

        public long SelectedSize => _selectedSize;
        public bool HasSelectedSpace => SelectedSize > 0;

        private void RecalculateSelectedSize()
        {
            long newValue = 0;
            foreach (var child in Children)
            {
                if (child.IsSelected) newValue += child.Size;
            }
            if (_selectedSize == newValue) return;

            _selectedSize = newValue;
            OnPropertyChanged(nameof(SelectedSize));
            OnPropertyChanged(nameof(HasSelectedSpace));
        }

        private void RecalculateTotalSizeAndLastChild()
        {
            long total = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                total += Children[i].Size;
                Children[i].IsLastChild = (i == Children.Count - 1);
            }
            if (_totalStrippableSize != total)
            {
                _totalStrippableSize = total;
                OnPropertyChanged(nameof(TotalStrippableSize));
            }
        }

        public StrippableModViewModel(ModItem mod)
        {
            Mod = mod;
            Children = new ObservableCollection<StrippableItemViewModel>();
            Children.CollectionChanged += (_, _) =>
            {
                RecalculateTotalSizeAndLastChild();
                OnPropertyChanged(nameof(ItemsCountBadge));
                OnPropertyChanged(nameof(IsExpandedWithChildren));
                RecalculateSelectedSize();
            };
            ToggleExpandCommand = ReactiveUI.ReactiveCommand.Create(ToggleExpand);
        }

        public void UpdateParentSelectionState()
        {
            _isUpdatingFromChildren = true;
            try
            {
                int selectedCount = 0;
                foreach (var child in Children)
                {
                    if (child.IsSelected) selectedCount++;
                }

                if (selectedCount == 0)
                    IsSelected = false;
                else if (selectedCount == Children.Count)
                    IsSelected = true;
                else
                    IsSelected = null;
            }
            finally
            {
                _isUpdatingFromChildren = false;
            }
            RecalculateSelectedSize();
        }
    }

    public class StripDialogViewModel : DialogViewModelBase<(bool, IEnumerable<string>?)>
    {
        private string _searchText = string.Empty;
        private StripSortMode _sortMode = StripSortMode.LargestFirst;
        private bool _isBulkSelecting;
        private bool _isBulkExpanding;
        private long _totalSelectedSize;
        private int _selectedModCount;

        public ObservableCollection<StrippableModViewModel> StrippableMods { get; }
        public BulkObservableCollection<StrippableModViewModel> FilteredMods { get; } = new();

        /// <summary>
        /// Flat virtualized row stream consumed by the ListBox: parent mod VMs
        /// interleaved with their child VMs only while expanded.
        /// </summary>
        public BulkObservableCollection<object> VisibleRows { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RefreshFilteredMods();
            }
        }

        public StripSortMode SortMode
        {
            get => _sortMode;
            set
            {
                if (SetProperty(ref _sortMode, value))
                {
                    OnPropertyChanged(nameof(IsSortLargestFirst));
                    OnPropertyChanged(nameof(IsSortNameAZ));
                    RefreshFilteredMods();
                }
            }
        }

        public bool IsSortLargestFirst => SortMode == StripSortMode.LargestFirst;
        public bool IsSortNameAZ => SortMode == StripSortMode.NameAZ;
        public bool HasNoFilteredMods => FilteredMods.Count == 0;

        public long TotalSelectedSize => _totalSelectedSize;
        public bool HasSelectedSpace => _totalSelectedSize > 0;

        public string FooterSelectionNote =>
            $"({_selectedModCount} of {StrippableMods.Count} mods · permanently deleted)";

        public ICommand StripCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand SetSortModeCommand { get; }
        public ICommand OpenInExplorerCommand { get; }

        public StripDialogViewModel(IEnumerable<StrippableModViewModel> mods) : base("Mods Stripping Dialog")
        {
            StrippableMods = new ObservableCollection<StrippableModViewModel>(mods);

            long maxBytes = 1L;
            foreach (var mod in StrippableMods)
            {
                if (mod.TotalStrippableSize > maxBytes)
                    maxBytes = mod.TotalStrippableSize;
            }

            foreach (var mod in StrippableMods)
            {
                mod.SizeSharePercent = Math.Max(4.0, (double)mod.TotalStrippableSize / maxBytes * 100.0);
                mod.PropertyChanged += OnModPropertyChanged;
            }

            RecalculateTotals(notify: false);

            CancelCommand = CreateCommand(() => CloseDialog((false, null)));
            StripCommand = CreateCommand(ExecuteStrip, () => HasSelectedSpace, nameof(HasSelectedSpace));
            SelectAllCommand = CreateCommand(() => SetAllSelection(true));
            SelectNoneCommand = CreateCommand(() => SetAllSelection(false));
            ExpandAllCommand = CreateCommand(() => SetAllExpanded(true));
            CollapseAllCommand = CreateCommand(() => SetAllExpanded(false));
            SetSortModeCommand = CreateCommand<string>(param =>
            {
                if (Enum.TryParse<StripSortMode>(param, out var mode))
                    SortMode = mode;
            });
            OpenInExplorerCommand = CreateCommand<string>(ExecuteOpenInExplorer);

            RefreshFilteredMods();
        }

        private void OnModPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StrippableModViewModel.SelectedSize))
            {
                if (!_isBulkSelecting)
                    RecalculateTotals(notify: true);
            }
            else if (e.PropertyName == nameof(StrippableModViewModel.IsExpanded) && sender is StrippableModViewModel mod)
            {
                if (!_isBulkExpanding)
                    ToggleModChildrenInVisibleRows(mod);
            }
        }

        private void RecalculateTotals(bool notify)
        {
            long total = 0;
            int activeMods = 0;
            foreach (var mod in StrippableMods)
            {
                long sz = mod.SelectedSize;
                total += sz;
                if (sz > 0) activeMods++;
            }

            bool totalChanged = _totalSelectedSize != total;
            bool countChanged = _selectedModCount != activeMods;

            _totalSelectedSize = total;
            _selectedModCount = activeMods;

            if (!notify) return;

            if (totalChanged)
            {
                OnPropertyChanged(nameof(TotalSelectedSize));
                OnPropertyChanged(nameof(HasSelectedSpace));
            }
            if (countChanged)
            {
                OnPropertyChanged(nameof(FooterSelectionNote));
            }
        }

        private void RefreshFilteredMods()
        {
            var term = SearchText?.Trim() ?? string.Empty;
            var filtered = string.IsNullOrEmpty(term)
                ? StrippableMods.AsEnumerable()
                : StrippableMods.Where(m =>
                    (m.Mod.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (m.Mod.PackageId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));

            var ordered = (SortMode == StripSortMode.LargestFirst
                ? filtered.OrderByDescending(m => m.TotalStrippableSize).ThenBy(m => m.Mod.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(m => m.Mod.DisplayName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (ordered.Count == FilteredMods.Count && ordered.SequenceEqual(FilteredMods))
                return;

            FilteredMods.ReplaceAll(ordered);
            OnPropertyChanged(nameof(HasNoFilteredMods));
            RebuildVisibleRows();
        }

        private void RebuildVisibleRows()
        {
            var rows = new List<object>(FilteredMods.Count);
            foreach (var mod in FilteredMods)
            {
                rows.Add(mod);
                if (mod.IsExpanded && mod.Children.Count > 0)
                {
                    for (int i = 0; i < mod.Children.Count; i++)
                    {
                        var child = mod.Children[i];
                        child.IsLastChild = (i == mod.Children.Count - 1);
                        rows.Add(child);
                    }
                }
            }
            VisibleRows.ReplaceAll(rows);
        }

        /// <summary>
        /// Updates only the toggled mod's slice in VisibleRows so single-mod
        /// expand/collapse does not reset scroll position.
        /// </summary>
        private void ToggleModChildrenInVisibleRows(StrippableModViewModel mod)
        {
            int idx = VisibleRows.IndexOf(mod);
            if (idx < 0) return;

            if (mod.IsExpanded)
            {
                for (int i = 0; i < mod.Children.Count; i++)
                {
                    var child = mod.Children[i];
                    child.IsLastChild = (i == mod.Children.Count - 1);
                    VisibleRows.Insert(idx + 1 + i, child);
                }
            }
            else
            {
                int removeAt = idx + 1;
                while (removeAt < VisibleRows.Count && VisibleRows[removeAt] is StrippableItemViewModel child && ReferenceEquals(child.Parent, mod))
                {
                    VisibleRows.RemoveAt(removeAt);
                }
            }
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
            _isBulkSelecting = true;
            try
            {
                foreach (var mod in StrippableMods)
                    mod.IsSelected = isSelected;
            }
            finally
            {
                _isBulkSelecting = false;
            }
            RecalculateTotals(notify: true);
        }

        private void SetAllExpanded(bool expanded)
        {
            _isBulkExpanding = true;
            try
            {
                foreach (var mod in StrippableMods)
                    mod.IsExpanded = expanded;
            }
            finally
            {
                _isBulkExpanding = false;
            }
            RebuildVisibleRows();
        }

        private void ExecuteOpenInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                RimSharp.Core.Services.SystemLauncher.RevealInFileManager(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StripDialogViewModel] Failed to open path in explorer: {ex.Message}");
            }
        }
    }
}