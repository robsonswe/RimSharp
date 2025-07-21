using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RimSharp.Core.Converters.ViewVisibility
{
    /// <summary>
    /// Converts a numeric value (int, long, etc.) or an ICollection's count to a Visibility value.
    /// Returns Visible if the count/value is greater than 0, otherwise Collapsed.
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long count = 0;

            if (value is ICollection collection)
            {
                count = collection.Count;
            }
            else if (value != null)
            {
                // Use System.Convert.ToInt64 to safely handle int, long, short, byte, etc.
                try
                {
                    count = System.Convert.ToInt64(value);
                }
                catch (Exception)
                {
                    // Value is not a convertible number, default to 0.
                    count = 0;
                }
            }

            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This is a one-way conversion, so we don't need to implement ConvertBack.
            return DependencyProperty.UnsetValue;
        }
    }
}