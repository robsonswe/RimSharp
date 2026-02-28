using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Numeric
{
    public class NumericToBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intVal) return intVal > 0;
            if (value is long longVal) return longVal > 0;
            if (value is double doubleVal) return doubleVal > 0;
            if (value is float floatVal) return floatVal > 0;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
