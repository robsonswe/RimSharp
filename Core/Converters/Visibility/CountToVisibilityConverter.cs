using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.ViewVisibility
{
    public class CountToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string) return false;
            
            long count = 0;
            if (value is ICollection collection)
            {
                count = collection.Count;
            }
            else if (value is IEnumerable enumerable)
            {
                count = enumerable.Cast<object>().Count();
            }
            else if (value != null)
            {
                try
                {
                    count = System.Convert.ToInt64(value);
                }
                catch
                {
                    count = 0;
                }
            }
            return count > 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

