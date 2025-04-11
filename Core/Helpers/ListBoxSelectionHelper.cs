using System.Collections; // Required for IList
using System.Collections.Generic; // Required for List
using System.Linq; // Required for Linq extensions like Cast()
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // Required for Selector

namespace RimSharp.Core.Helpers
{
    public static class ListBoxSelectionHelper
    {
        // --- Existing IsSelectionHighlightEnabled Property ---

        public static readonly DependencyProperty IsSelectionHighlightEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsSelectionHighlightEnabled",
                typeof(bool),
                typeof(ListBoxSelectionHelper),
                new PropertyMetadata(true));

        public static bool GetIsSelectionHighlightEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsSelectionHighlightEnabledProperty);
        }

        public static void SetIsSelectionHighlightEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsSelectionHighlightEnabledProperty, value);
        }

        // --- NEW SelectedItems Attached Property ---

        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItems",                                    // Name
                typeof(IList),                                      // Type of the property (binds to ViewModel's IList)
                typeof(ListBoxSelectionHelper),                     // Owner class
                new FrameworkPropertyMetadata(                      // Use FrameworkPropertyMetadata for binding capabilities
                    null,                                           // Default value
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, // Allow TwoWay binding
                    OnSelectedItemsChanged));                       // Callback when the property changes

        public static IList GetSelectedItems(DependencyObject obj)
        {
            return (IList)obj.GetValue(SelectedItemsProperty);
        }

        public static void SetSelectedItems(DependencyObject obj, IList value)
        {
            obj.SetValue(SelectedItemsProperty, value);
        }

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Ensure we're attached to a ListBox (or Selector)
            if (!(d is ListBox listBox)) // Or Selector if you want broader compatibility
                return;

            // Unsubscribe from previous list's event, subscribe to new one
            // Although the ListBox instance itself doesn't change often here, it's good practice
            listBox.SelectionChanged -= ListBox_SelectionChanged;
            if (e.NewValue != null)
            {
                listBox.SelectionChanged += ListBox_SelectionChanged;

                // Sync initial selection FROM the ViewModel TO the ListBox, if needed
                // (This part handles if the ViewModel sets the selection programmatically)
                // Be cautious with TwoWay binding to avoid infinite loops if not handled carefully.
                // Since the XAML uses OneWayToSource, we primarily care about ListBox -> ViewModel updates here.
                 UpdateListBoxSelection(listBox);
            }
            // If e.NewValue is null, we just stop listening.
        }

        private static void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ListBox listBox))
                return;

            // Get the current value of our attached property (the IList bound to the ViewModel)
            IList currentBoundList = GetSelectedItems(listBox);

            // Update the bound list IN PLACE if possible, or create a new list
            // This triggers the binding update back to the ViewModel
            if (currentBoundList != null)
            {
                // Using a temporary list prevents issues if the source list changes during iteration
                var listBoxSelectedItems = listBox.SelectedItems.Cast<object>().ToList();

                // Efficiently update the bound list (more complex for full TwoWay, simple for OneWayToSource)
                SetSelectedItems(listBox, listBoxSelectedItems); // Replace the bound list instance
            }
        }

         // Helper to sync ListBox selection based on the bound IList
         // (More important for TwoWay binding, less critical for OneWayToSource from XAML)
         private static void UpdateListBoxSelection(ListBox listBox)
        {
            IList boundSelectedItems = GetSelectedItems(listBox);
            if (boundSelectedItems == null) return;

            // Temporarily unsubscribe to prevent feedback loop during programmatic selection
            listBox.SelectionChanged -= ListBox_SelectionChanged;

            try
            {
                listBox.SelectedItems.Clear(); // Clear current ListBox selection
                if (boundSelectedItems.Count > 0)
                {
                    // Add items from the bound list to the ListBox selection
                    foreach (var item in boundSelectedItems)
                    {
                         // Check if the item actually exists in the ListBox's ItemsSource
                        if (listBox.Items.Contains(item))
                        {
                            listBox.SelectedItems.Add(item);
                        }
                    }
                }
            }
            finally
            {
                 // Re-subscribe
                listBox.SelectionChanged += ListBox_SelectionChanged;
            }
        }
    }
}