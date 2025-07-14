using System;
using System.Collections; // For IEnumerable and IList
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;

namespace RimSharp.Features.ModManager.Components.ModList
{
    public partial class ModListView : UserControl
    {
        public IList SelectedItems
        {
            get { return (IList)GetValue(SelectedItemsProperty); }
            set { SetValue(SelectedItemsProperty, value); }
        }

        public ModListView()
        {
            InitializeComponent();
            SearchPlaceholder = $"Search {HeaderText}...";
            FilterToolTip = $"Filter {HeaderText} mods";
        }

        // --- Dependency Properties (No changes to definitions) ---
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.Register("SelectedItems", typeof(IList), typeof(ModListView),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(ModListView), new PropertyMetadata(null));

        public object SelectedItem
        {
            get { return (object)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(ModListView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public string HeaderText { get { return (string)GetValue(HeaderTextProperty); } set { SetValue(HeaderTextProperty, value); } }
        public static readonly DependencyProperty HeaderTextProperty = DependencyProperty.Register("HeaderText", typeof(string), typeof(ModListView), new PropertyMetadata("Items", OnHeaderTextChanged));
        public int ItemCount { get { return (int)GetValue(ItemCountProperty); } set { SetValue(ItemCountProperty, value); } }
        public static readonly DependencyProperty ItemCountProperty = DependencyProperty.Register("ItemCount", typeof(int), typeof(ModListView), new PropertyMetadata(0));
        public string SearchText { get { return (string)GetValue(SearchTextProperty); } set { SetValue(SearchTextProperty, value); } }
        public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register("SearchText", typeof(string), typeof(ModListView), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public ICommand DoubleClickCommand { get { return (ICommand)GetValue(DoubleClickCommandProperty); } set { SetValue(DoubleClickCommandProperty, value); } }
        public static readonly DependencyProperty DoubleClickCommandProperty = DependencyProperty.Register("DoubleClickCommand", typeof(ICommand), typeof(ModListView), new PropertyMetadata(null));
        public ICommand FilterCommand { get { return (ICommand)GetValue(FilterCommandProperty); } set { SetValue(FilterCommandProperty, value); } }
        public static readonly DependencyProperty FilterCommandProperty = DependencyProperty.Register("FilterCommand", typeof(ICommand), typeof(ModListView), new PropertyMetadata(null));
        public string GroupName { get { return (string)GetValue(GroupNameProperty); } set { SetValue(GroupNameProperty, value); } }
        public static readonly DependencyProperty GroupNameProperty = DependencyProperty.Register("GroupName", typeof(string), typeof(ModListView), new PropertyMetadata("DefaultGroup"));
        public ICommand DropCommand { get { return (ICommand)GetValue(DropCommandProperty); } set { SetValue(DropCommandProperty, value); } }
        public static readonly DependencyProperty DropCommandProperty = DependencyProperty.Register("DropCommand", typeof(ICommand), typeof(ModListView), new PropertyMetadata(null));
        public Type DragItemType { get { return (Type)GetValue(DragItemTypeProperty); } set { SetValue(DragItemTypeProperty, value); } }
        public static readonly DependencyProperty DragItemTypeProperty = DependencyProperty.Register("DragItemType", typeof(Type), typeof(ModListView), new PropertyMetadata(null));
        private static readonly DependencyPropertyKey SearchPlaceholderPropertyKey = DependencyProperty.RegisterReadOnly("SearchPlaceholder", typeof(string), typeof(ModListView), new PropertyMetadata("Search..."));
        public static readonly DependencyProperty SearchPlaceholderProperty = SearchPlaceholderPropertyKey.DependencyProperty;
        public string SearchPlaceholder { get { return (string)GetValue(SearchPlaceholderProperty); } private set { SetValue(SearchPlaceholderPropertyKey, value); } }
        private static readonly DependencyPropertyKey FilterToolTipPropertyKey = DependencyProperty.RegisterReadOnly("FilterToolTip", typeof(string), typeof(ModListView), new PropertyMetadata("Filter items"));
        public static readonly DependencyProperty FilterToolTipProperty = FilterToolTipPropertyKey.DependencyProperty;
        public string FilterToolTip { get { return (string)GetValue(FilterToolTipProperty); } private set { SetValue(FilterToolTipPropertyKey, value); } }


        private static void OnHeaderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ModListView view && e.NewValue is string header)
            {
                view.SearchPlaceholder = $"Search {header}...";
                view.FilterToolTip = $"Filter {header} mods";
            }
        }
        
        // MODIFICATION 1: The INCOMING change handler from the ViewModel.
        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ModListView)d;
            var newValue = e.NewValue;

            // Guard clause to prevent feedback loop. If the internal ListBox already has this value, do nothing.
            if (control.InternalListBox.SelectedItem == newValue)
            {
                return;
            }

            // The ViewModel has changed the selection.
            // Check if the new item exists in our list. If not, deselect. Otherwise, select it.
            var itemsSource = control.InternalListBox.ItemsSource as IEnumerable;
            bool itemExistsInThisList = itemsSource?.Cast<object>().Contains(newValue) ?? false;

            control.InternalListBox.SelectedItem = itemExistsInThisList ? newValue : null;
        }
        
        // MODIFICATION 2: The OUTGOING change handler from the ListBox.
        private void InternalListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Sync multi-select properties. This is always needed for context menus etc.
            var currentSelectedItems = InternalListBox.SelectedItems.Cast<object>().ToList();
            var holder = (SelectedItemsHolder)Resources["selectedItemsHolder"];
            holder.SelectedItems = currentSelectedItems;
            this.SelectedItems = currentSelectedItems;
            
            // Sync the single selected item.
            var currentSelectedItem = InternalListBox.SelectedItem;
            
            // Guard clause to prevent feedback loop. If the DP already has this value, do nothing.
            if (this.SelectedItem == currentSelectedItem)
            {
                return;
            }
            
            // The user has changed the selection, so update the Dependency Property.
            // This will propagate the change to the ViewModel.
            this.SelectedItem = currentSelectedItem;
        }

        private void InternalListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ItemsControl, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null && DoubleClickCommand != null && DoubleClickCommand.CanExecute(item.DataContext))
            {
                DoubleClickCommand.Execute(item.DataContext);
            }
        }
        
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == SelectedItemProperty && e.NewValue != null)
            {
                InternalListBox.ScrollIntoView(e.NewValue);
            }
        }
    }
}