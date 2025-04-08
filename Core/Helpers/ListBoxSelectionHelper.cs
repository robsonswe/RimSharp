using System.Windows;
using System.Windows.Controls;

namespace RimSharp.Core.Helpers
{
    public static class ListBoxSelectionHelper
    {
        // Define the attached property
        public static readonly DependencyProperty IsSelectionHighlightEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsSelectionHighlightEnabled", // Property name
                typeof(bool),                  // Property type
                typeof(ListBoxSelectionHelper), // Owner class
                new PropertyMetadata(true)); // Default value: true (highlighting enabled by default)

        // Getter method
        public static bool GetIsSelectionHighlightEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsSelectionHighlightEnabledProperty);
        }

        // Setter method
        public static void SetIsSelectionHighlightEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsSelectionHighlightEnabledProperty, value);
        }
    }
}
