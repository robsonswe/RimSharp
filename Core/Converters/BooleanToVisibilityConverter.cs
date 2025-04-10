using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RimSharp.Core.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        // --- ADD THIS PROPERTY ---
        /// <summary>
        /// Gets or sets a value indicating whether the conversion should be inverted
        /// (true -> Collapsed, false -> Visible).
        /// </summary>
        public bool Inverse { get; set; }
        // --- END OF ADDITION ---

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // --- MODIFY THIS LOGIC ---
            bool boolValue = false;
            if (value is bool b)
            {
                boolValue = b;
            }
            // If Inverse is true, swap the logic
            if (Inverse)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            // --- END OF MODIFICATION ---
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ConvertBack logic might also need inversion if you implement it fully,
            // but it's often not needed for simple visibility.
            // For now, we keep it simple. If you needed ConvertBack:
            /*
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                return Inverse ? !result : result;
            }
            return DependencyProperty.UnsetValue; // Or false/default
            */
            throw new NotImplementedException();
        }
    }
}
