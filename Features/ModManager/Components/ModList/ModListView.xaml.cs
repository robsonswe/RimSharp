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
        public ModListView()
        {
            InitializeComponent();
            // Set default values for derived properties
            SearchPlaceholder = $"Search {HeaderText}...";
            FilterToolTip = $"Filter {HeaderText} mods";
        }

        // --- Dependency Properties ---

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
            DependencyProperty.Register("SelectedItem", typeof(object), typeof(ModListView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string HeaderText
        {
            get { return (string)GetValue(HeaderTextProperty); }
            set { SetValue(HeaderTextProperty, value); }
        }
        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register("HeaderText", typeof(string), typeof(ModListView), new PropertyMetadata("Items", OnHeaderTextChanged));

        public int ItemCount
        {
            get { return (int)GetValue(ItemCountProperty); }
            set { SetValue(ItemCountProperty, value); }
        }
        public static readonly DependencyProperty ItemCountProperty =
            DependencyProperty.Register("ItemCount", typeof(int), typeof(ModListView), new PropertyMetadata(0));

        public string SearchText
        {
            get { return (string)GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register("SearchText", typeof(string), typeof(ModListView), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ICommand DoubleClickCommand
        {
            get { return (ICommand)GetValue(DoubleClickCommandProperty); }
            set { SetValue(DoubleClickCommandProperty, value); }
        }
        public static readonly DependencyProperty DoubleClickCommandProperty =
            DependencyProperty.Register("DoubleClickCommand", typeof(ICommand), typeof(ModListView), new PropertyMetadata(null));

        public ICommand FilterCommand
        {
            get { return (ICommand)GetValue(FilterCommandProperty); }
            set { SetValue(FilterCommandProperty, value); }
        }
        public static readonly DependencyProperty FilterCommandProperty =
            DependencyProperty.Register("FilterCommand", typeof(ICommand), typeof(ModListView), new PropertyMetadata(null));

        // GroupName DP (used by Behavior)
        public string GroupName
        {
            get { return (string)GetValue(GroupNameProperty); }
            set { SetValue(GroupNameProperty, value); }
        }
        public static readonly DependencyProperty GroupNameProperty =
            DependencyProperty.Register("GroupName", typeof(string), typeof(ModListView), new PropertyMetadata("DefaultGroup"));

        // --- Dependency Properties needed by the Behavior ---
        public ICommand DropCommand
        {
            get { return (ICommand)GetValue(DropCommandProperty); }
            set { SetValue(DropCommandProperty, value); }
        }
        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.Register("DropCommand", typeof(ICommand), typeof(ModListView), new PropertyMetadata(null));

        public Type DragItemType
        {
            get { return (Type)GetValue(DragItemTypeProperty); }
            set { SetValue(DragItemTypeProperty, value); }
        }
        public static readonly DependencyProperty DragItemTypeProperty =
            DependencyProperty.Register("DragItemType", typeof(Type), typeof(ModListView), new PropertyMetadata(null));

        // --- Derived Read-only Dependency Properties for UI ---

        private static readonly DependencyPropertyKey SearchPlaceholderPropertyKey =
            DependencyProperty.RegisterReadOnly("SearchPlaceholder", typeof(string), typeof(ModListView), new PropertyMetadata("Search..."));
        public static readonly DependencyProperty SearchPlaceholderProperty = SearchPlaceholderPropertyKey.DependencyProperty;
        public string SearchPlaceholder
        {
            get { return (string)GetValue(SearchPlaceholderProperty); }
            private set { SetValue(SearchPlaceholderPropertyKey, value); }
        }

        private static readonly DependencyPropertyKey FilterToolTipPropertyKey =
            DependencyProperty.RegisterReadOnly("FilterToolTip", typeof(string), typeof(ModListView), new PropertyMetadata("Filter items"));
        public static readonly DependencyProperty FilterToolTipProperty = FilterToolTipPropertyKey.DependencyProperty;
        public string FilterToolTip
        {
            get { return (string)GetValue(FilterToolTipProperty); }
            private set { SetValue(FilterToolTipPropertyKey, value); }
        }

        // Update derived properties when HeaderText changes
        private static void OnHeaderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ModListView view && e.NewValue is string header)
            {
                view.SearchPlaceholder = $"Search {header}...";
                view.FilterToolTip = $"Filter {header} mods";
            }
        }

        // --- Event Handlers ---

        private void InternalListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Find the ListBoxItem that was clicked
            var item = ItemsControl.ContainerFromElement(sender as ItemsControl, e.OriginalSource as DependencyObject) as ListBoxItem;

            if (item != null && DoubleClickCommand != null)
            {
                // Get the data item (ModItem)
                object dataItem = item.DataContext;
                if (dataItem != null && DoubleClickCommand.CanExecute(dataItem))
                {
                    DoubleClickCommand.Execute(dataItem);
                }
            }
        }

        private void InternalListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var holder = (SelectedItemsHolder)Resources["selectedItemsHolder"];
            holder.SelectedItems = InternalListBox.SelectedItems.Cast<object>().ToList();
        }

        // Optional: Helper to scroll item into view when selected
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