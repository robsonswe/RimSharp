using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RimSharp.Core.Converters.Visual
{
    public class BooleanToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string keys)
            {
                var parts = keys.Split('|');
                if (parts.Length == 2)
                {
                    string key = boolValue ? parts[0] : parts[1];
                    
                    if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true ||
                        Application.Current?.Resources.TryGetResource(key + "Brush", null, out resource) == true)
                    {
                        if (resource is Color color) return new SolidColorBrush(color);
                        if (resource is IBrush brush) return brush;
                    }
                }
            }
            return Brushes.Transparent; // Fallback
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

