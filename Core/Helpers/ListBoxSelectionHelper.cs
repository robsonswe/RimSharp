using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RimSharp.Core.Helpers
{
    public static class ListBoxSelectionHelper
    {
        public static readonly AttachedProperty<bool> IsSelectionHighlightEnabledProperty =
            AvaloniaProperty.RegisterAttached<ListBox, bool>("IsSelectionHighlightEnabled", typeof(ListBoxSelectionHelper), true);

        public static bool GetIsSelectionHighlightEnabled(ListBox obj) => obj.GetValue(IsSelectionHighlightEnabledProperty);
        public static void SetIsSelectionHighlightEnabled(ListBox obj, bool value) => obj.SetValue(IsSelectionHighlightEnabledProperty, value);

        public static readonly AttachedProperty<IList?> SelectedItemsProperty =
            AvaloniaProperty.RegisterAttached<ListBox, IList?>("SelectedItems", typeof(ListBoxSelectionHelper));

        public static IList? GetSelectedItems(ListBox obj) => obj.GetValue(SelectedItemsProperty);
        public static void SetSelectedItems(ListBox obj, IList? value) => obj.SetValue(SelectedItemsProperty, value);

        static ListBoxSelectionHelper()
        {
            SelectedItemsProperty.Changed.AddClassHandler<ListBox>(OnSelectedItemsChanged);
        }

        private static void OnSelectedItemsChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
        {
            listBox.SelectionChanged -= ListBox_SelectionChanged;
            if (e.NewValue != null)
            {
                listBox.SelectionChanged += ListBox_SelectionChanged;
            }
        }

        private static void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var selectedItems = listBox.SelectedItems?.Cast<object>().ToList();
                SetSelectedItems(listBox, selectedItems);
            }
        }
    }
}
