using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace RimSharp.Features.ModManager.Components.ModList
{
    public partial class ModListView : UserControl
    {
        private ListBox? _internalListBox;
        
        public ModListView()
        {
            InitializeComponent();
            this.GetObservable(HeaderTextProperty).Subscribe(header =>
            {
                SearchPlaceholder = $"Search {header}...";
                FilterToolTip = $"Filter {header} mods";
            });

            this.GetObservable(ItemsSourceProperty).Subscribe(_ =>
            {
                // When ItemsSource changes (filtering/reset), we want to re-sync selection.
                _isHandlingFiltering = true;
                Dispatcher.UIThread.Post(() => {
                    try {
                        SyncInternalSelection();
                    } finally {
                        _isHandlingFiltering = false;
                    }
                }, DispatcherPriority.Loaded);
            });

            // Attach event handlers
            _internalListBox = this.FindControl<ListBox>("InternalListBox");
            if (_internalListBox != null)
            {
                _internalListBox.SelectionChanged += InternalListBox_SelectionChanged;
                _internalListBox.AddHandler(InputElement.PointerPressedEvent, InternalListBox_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            }
        }

        private void InternalListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var properties = e.GetCurrentPoint(null).Properties;
            if (e.ClickCount == 2 && properties.IsLeftButtonPressed)
            {
                var item = _internalListBox?.SelectedItem;
                if (item != null && DoubleClickCommand != null && DoubleClickCommand.CanExecute(item))
                {
                    DoubleClickCommand.Execute(item);
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private bool _isUpdatingFromDP;
        private bool _isUpdatingToDP;
        private bool _isHandlingFiltering;

        private void SyncInternalSelection()
        {
            if (_internalListBox == null || _isUpdatingToDP) return;
            
            _isUpdatingFromDP = true;
            try
            {
                var targetValue = this.SelectedItem;
                object? itemInList = null;
                
                if (targetValue != null)
                {
                    foreach (var item in _internalListBox.Items)
                    {
                        if (Equals(item, targetValue))
                        {
                            itemInList = item;
                            break;
                        }
                    }
                }

                if (itemInList != null)
                {
                    if (!Equals(_internalListBox.SelectedItem, itemInList))
                    {
                        _internalListBox.SelectedItem = itemInList;
                        _internalListBox.ScrollIntoView(itemInList);
                    }
                }
                else
                {
                    if (_internalListBox.SelectedItem != null)
                    {
                        _internalListBox.SelectedItem = null;
                    }
                }
            }
            finally
            {
                _isUpdatingFromDP = false;
            }
        }
        
        private void InternalListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_internalListBox == null || _isUpdatingFromDP) return;
            
            // If we are currently handling a filter change, ignore the ListBox's automatic selection clearing.
            if (_isHandlingFiltering) return;

            var newSelection = _internalListBox.SelectedItem;
            var currentGlobalSelection = this.SelectedItem;

            // If the ListBox cleared its selection, but we had one.
            if (newSelection == null && currentGlobalSelection != null)
            {
                bool itemIsStillInList = false;
                foreach (var item in _internalListBox.Items)
                {
                    if (Equals(item, currentGlobalSelection))
                    {
                        itemIsStillInList = true;
                        break;
                    }
                }

                // If the item disappeared from the list, we return early to "remember" it globally.
                if (!itemIsStillInList)
                {
                    UpdateSelectedItems(new List<object>());
                    return; 
                }
                else
                {
                    // Item IS in the list, but ListBox cleared selection.
                    // This can happen during internal ListBox updates.
                    // We re-apply the selection.
                    SyncInternalSelection();
                    return;
                }
            }

            _isUpdatingToDP = true;
            try
            {
                if (!Equals(this.SelectedItem, newSelection))
                {
                    this.SelectedItem = newSelection;
                }
                UpdateSelectedItems(_internalListBox.SelectedItems?.Cast<object>().ToList() ?? new List<object>());
            }
            finally
            {
                _isUpdatingToDP = false;
            }
        }

        private void UpdateSelectedItems(IList items)
        {
            this.SelectedItems = items;
            if (Resources.TryGetValue("selectedItemsHolder", out var holderObj) && holderObj is SelectedItemsHolder holder)
            {
                holder.SelectedItems = items;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SelectedItemProperty)
            {
                SyncInternalSelection();
            }
        }

        public static readonly StyledProperty<IList?> SelectedItemsProperty =
            AvaloniaProperty.Register<ModListView, IList?>(nameof(SelectedItems));

        public IList? SelectedItems
        {
            get => GetValue(SelectedItemsProperty);
            set => SetValue(SelectedItemsProperty, value);
        }

        public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
            AvaloniaProperty.Register<ModListView, IEnumerable?>(nameof(ItemsSource));

        public IEnumerable? ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly StyledProperty<object?> SelectedItemProperty =
            AvaloniaProperty.Register<ModListView, object?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly StyledProperty<string> HeaderTextProperty =
            AvaloniaProperty.Register<ModListView, string>(nameof(HeaderText), "Items");

        public string HeaderText
        {
            get => GetValue(HeaderTextProperty);
            set => SetValue(HeaderTextProperty, value);
        }

        public static readonly StyledProperty<int> ItemCountProperty =
            AvaloniaProperty.Register<ModListView, int>(nameof(ItemCount), 0);

        public int ItemCount
        {
            get => GetValue(ItemCountProperty);
            set => SetValue(ItemCountProperty, value);
        }

        public static readonly StyledProperty<string> SearchTextProperty =
            AvaloniaProperty.Register<ModListView, string>(nameof(SearchText), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public string SearchText
        {
            get => GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        public static readonly StyledProperty<ICommand?> DoubleClickCommandProperty =
            AvaloniaProperty.Register<ModListView, ICommand?>(nameof(DoubleClickCommand));

        public ICommand? DoubleClickCommand
        {
            get => GetValue(DoubleClickCommandProperty);
            set => SetValue(DoubleClickCommandProperty, value);
        }

        public static readonly StyledProperty<bool> IsFilterAppliedProperty =
            AvaloniaProperty.Register<ModListView, bool>(nameof(IsFilterApplied), false);

        public bool IsFilterApplied
        {
            get => GetValue(IsFilterAppliedProperty);
            set => SetValue(IsFilterAppliedProperty, value);
        }

        public static readonly StyledProperty<ICommand?> FilterCommandProperty =
            AvaloniaProperty.Register<ModListView, ICommand?>(nameof(FilterCommand));

        public ICommand? FilterCommand
        {
            get => GetValue(FilterCommandProperty);
            set => SetValue(FilterCommandProperty, value);
        }

        public static readonly DirectProperty<ModListView, string> SearchPlaceholderProperty =
            AvaloniaProperty.RegisterDirect<ModListView, string>(nameof(SearchPlaceholder), o => o.SearchPlaceholder);

        private string _searchPlaceholder = "Search...";
        public string SearchPlaceholder
        {
            get => _searchPlaceholder;
            private set => SetAndRaise(SearchPlaceholderProperty, ref _searchPlaceholder, value);
        }

        public static readonly DirectProperty<ModListView, string> FilterToolTipProperty =
            AvaloniaProperty.RegisterDirect<ModListView, string>(nameof(FilterToolTip), o => o.FilterToolTip);

        private string _filterToolTip = "Filter items";
        public string FilterToolTip
        {
            get => _filterToolTip;
            private set => SetAndRaise(FilterToolTipProperty, ref _filterToolTip, value);
        }

        public static readonly StyledProperty<string> GroupNameProperty =
            AvaloniaProperty.Register<ModListView, string>(nameof(GroupName), "DefaultGroup");

        public string GroupName
        {
            get => GetValue(GroupNameProperty);
            set => SetValue(GroupNameProperty, value);
        }

        public static readonly StyledProperty<ICommand?> DropCommandProperty =
            AvaloniaProperty.Register<ModListView, ICommand?>(nameof(DropCommand));

        public ICommand? DropCommand
        {
            get => GetValue(DropCommandProperty);
            set => SetValue(DropCommandProperty, value);
        }

        public static readonly StyledProperty<Type?> DragItemTypeProperty =
            AvaloniaProperty.Register<ModListView, Type?>(nameof(DragItemType));

        public Type? DragItemType
        {
            get => GetValue(DragItemTypeProperty);
            set => SetValue(DragItemTypeProperty, value);
        }

        public static readonly StyledProperty<SelectionMode> SelectionModeProperty =
            AvaloniaProperty.Register<ModListView, SelectionMode>(nameof(SelectionMode), SelectionMode.Multiple);

        public SelectionMode SelectionMode
        {
            get => GetValue(SelectionModeProperty);
            set => SetValue(SelectionModeProperty, value);
        }

        private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            ClearAllFocus();
        }

        private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ClearAllFocus();
            }
        }

        private void ClearAllFocus()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            topLevel?.FocusManager?.ClearFocus();
        }
    }
}
