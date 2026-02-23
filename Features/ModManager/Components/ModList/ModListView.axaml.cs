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

            // Attach event handlers - use FindControl since x:Name might not be generated
            _internalListBox = this.FindControl<ListBox>("InternalListBox");
            System.Diagnostics.Debug.WriteLine($"ModListView ctor: InternalListBox={_internalListBox}");
            if (_internalListBox != null)
            {
                System.Diagnostics.Debug.WriteLine("ModListView: Attaching event handlers");
                _internalListBox.SelectionChanged += InternalListBox_SelectionChanged;
                _internalListBox.AddHandler(InputElement.PointerPressedEvent, InternalListBox_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ModListView: InternalListBox is NULL!");
            }
        }

        private void InternalListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"PointerPressed: ClickCount={e.ClickCount}, Button={e.GetCurrentPoint(null).Properties.PointerUpdateKind}");
            
            // Only handle left button double-click
            var properties = e.GetCurrentPoint(null).Properties;
            if (e.ClickCount == 2 && properties.IsLeftButtonPressed)
            {
                System.Diagnostics.Debug.WriteLine($"Left double-click detected! SelectedItem={_internalListBox?.SelectedItem}, DoubleClickCommand={DoubleClickCommand}");
                
                var item = _internalListBox?.SelectedItem;
                
                if (item != null && DoubleClickCommand != null && DoubleClickCommand.CanExecute(item))
                {
                    System.Diagnostics.Debug.WriteLine($"Executing DoubleClickCommand with item: {item}");
                    DoubleClickCommand.Execute(item);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"NOT executing: item=null:{item == null}, Command=null:{DoubleClickCommand == null}, CanExecute:{DoubleClickCommand?.CanExecute(item)}");
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private bool _isUpdatingFromDP;
        private bool _isUpdatingToDP;
        
        // MODIFICATION 1: The INCOMING change handler from the ViewModel.
        private void HandleSelectedItemChanged(object? newValue)
        {
            // Prevent feedback loop
            if (_isUpdatingToDP)
                return;
                
            _isUpdatingFromDP = true;

            try
            {
                if (_internalListBox == null) return;

                // The ViewModel has changed the selection.
                // Check if the new item exists in our list. If not, deselect. Otherwise, select it.
                var itemsSource = _internalListBox.ItemsSource as IEnumerable;
                bool itemExistsInThisList = false;
                
                if (newValue != null && itemsSource != null)
                {
                    foreach (var item in itemsSource)
                    {
                        if (item == newValue)
                        {
                            itemExistsInThisList = true;
                            break;
                        }
                    }
                }

                // Update the ListBox's selection
                if (itemExistsInThisList)
                {
                    if (_internalListBox.SelectedItem != newValue)
                    {
                        _internalListBox.SelectedItem = newValue;
                    }
                }
                else
                {
                    // If the new value is NOT in this list, we should only clear our selection
                    // if our current selection IS NOT NULL.
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
        
        // MODIFICATION 2: The OUTGOING change handler from the ListBox.
        private void InternalListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_internalListBox == null) return;
            
            // Prevent feedback loop
            if (_isUpdatingFromDP)
                return;
                
            _isUpdatingToDP = true;
            
            try
            {
                // Sync the single selected item.
                var currentSelectedItem = _internalListBox.SelectedItem;

                // If items were removed AND no items were added, this is a deselection.
                // Check if this is because another list selected an item.
                if (e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
                {
                    // If the current global SelectedItem exists in this list but is not what we have now,
                    // it means we are clearing a real selection.
                    // If it DOES NOT exist in this list, it means someone else owns it, so we stay quiet.
                    var globalSelectedItem = this.SelectedItem;
                    if (globalSelectedItem != null)
                    {
                        var itemsSource = _internalListBox.ItemsSource as IEnumerable;
                        bool globalItemExistsInThisList = false;
                        if (itemsSource != null)
                        {
                            foreach (var item in itemsSource)
                            {
                                if (item == globalSelectedItem)
                                {
                                    globalItemExistsInThisList = true;
                                    break;
                                }
                            }
                        }
                        
                        if (!globalItemExistsInThisList)
                        {
                            // The global selection is not ours anyway, don't push our null.
                            return;
                        }
                    }
                }
                
                // Sync multi-select properties. This is always needed for context menus etc.
                var currentSelectedItems = _internalListBox.SelectedItems?.Cast<object>().ToList() ?? new List<object>();
                this.SelectedItems = currentSelectedItems;
                
                // Update the SelectedItemsHolder resource for context menu bindings
                if (Resources.TryGetValue("selectedItemsHolder", out var holderObj) && holderObj is SelectedItemsHolder holder)
                {
                    holder.SelectedItems = currentSelectedItems;
                }

                // Update the DP (this will trigger property changed notifications)
                if (this.SelectedItem != currentSelectedItem)
                {
                    this.SelectedItem = currentSelectedItem;
                }
            }
            finally
            {
                _isUpdatingToDP = false;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SelectedItemProperty)
            {
                var newValue = change.GetNewValue<object?>();
                HandleSelectedItemChanged(newValue);
                if (newValue != null)
                {
                    _internalListBox?.ScrollIntoView(newValue);
                }
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
    }
}
