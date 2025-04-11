using System;
using System.Globalization;
using System.Windows.Data;
using System.Collections;

namespace RimSharp.Core.Converters.Logic
{
    public class CountToBooleanConverter : IValueConverter
    {
        // Returns true if the count is greater than 0
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0;
            }
            // Optional: Handle other collection types if needed
            if (value is ICollection collection)
            {
                 return collection.Count > 0;
            }
            return false; // Default to false if type is wrong or collection is null/doesn't have count
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not needed for IsEnabled binding
            throw new NotImplementedException();
        }
    }
}
