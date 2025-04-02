using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace RimSharp.Views.Converters
{
    public class BooleanToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool boolValue)) return DependencyProperty.UnsetValue;
            if (!(parameter is string param)) return DependencyProperty.UnsetValue;

            var parts = param.Split('|');
            if (parts.Length != 2) return DependencyProperty.UnsetValue;

            try
            {
                var resourceKey = boolValue ? parts[0].Trim() : parts[1].Trim();
                if (string.IsNullOrEmpty(resourceKey)) return DependencyProperty.UnsetValue;

                // Try to find the resource exactly as specified
                var resource = Application.Current.TryFindResource(resourceKey);
                if (resource is Brush brush) return brush;
                if (resource is Color color) return new SolidColorBrush(color);

                // If not found, try common variations
                if (resourceKey.EndsWith("Brush"))
                {
                    var colorKey = resourceKey.Substring(0, resourceKey.Length - 6);
                    resource = Application.Current.TryFindResource(colorKey);
                    if (resource is Color c) return new SolidColorBrush(c);
                }
                else
                {
                    var brushKey = resourceKey + "Brush";
                    resource = Application.Current.TryFindResource(brushKey);
                    if (resource is Brush b) return b;
                }

                // Final fallback - try direct color parsing
                if (ColorConverter.ConvertFromString(resourceKey) is Color parsedColor)
                {
                    return new SolidColorBrush(parsedColor);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BooleanToColorConverter error: {ex.Message}");
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}