using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters
{
    public class IsNotNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Returns true if the value is not null AND not an empty/whitespace string
            // Use string.IsNullOrWhiteSpace for robustness if desired
            return value != null && !string.IsNullOrEmpty(value.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException(); // Not needed for one-way check
        }
    }
}