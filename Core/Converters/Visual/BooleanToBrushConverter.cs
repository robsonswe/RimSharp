using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RimSharp.Core.Converters.Visual
{
    public class BooleanToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string keys)
            {
                var parts = keys.Split('|');
                if (parts.Length == 2)
                {
                    string key = boolValue ? parts[1] : parts[0];
                    if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
                    {
                        return resource as IBrush;
                    }
                }
            }
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

