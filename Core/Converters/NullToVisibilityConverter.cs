using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RimSharp.Core.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        // Converts null to Collapsed, non-null to Visible
        // Parameter can invert this logic if set to "Invert"
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = (value == null);
            bool invert = (parameter as string)?.Equals("Invert", StringComparison.OrdinalIgnoreCase) ?? false;

            if (invert) // If inverted, visible when null
            {
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }
            else // Default: visible when NOT null
            {
                return isNull ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not needed for one-way binding
            throw new NotImplementedException();
        }
    }
}