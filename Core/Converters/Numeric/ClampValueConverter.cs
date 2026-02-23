using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Numeric
{
    public class ClampValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double val && parameter is string range)
            {
                var parts = range.Split(',');
                if (parts.Length == 2 && double.TryParse(parts[0], out var min) && double.TryParse(parts[1], out var max))
                {
                    // Scale from 0-1000 to min-max
                    double scaled = min + (max - min) * (val / 1000.0);
                    return Math.Max(min, Math.Min(max, scaled));
                }
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

