using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Logic
{
    public class CountToBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int i) return i > 0;
            if (value is long l) return l > 0;
            if (value is double d) return d > 0;
            if (value is decimal m) return m > 0;
            if (value is ICollection collection) return collection.Count > 0;
            if (value is IEnumerable enumerable) return enumerable.Cast<object>().Any();
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

