using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Visual
{
    public class BooleanToDoubleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && parameter is string values)
            {
                var parts = values.Split('|');
                if (parts.Length == 2 && double.TryParse(parts[0], out var trueVal) && double.TryParse(parts[1], out var falseVal))
                {
                    return b ? trueVal : falseVal;
                }
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

