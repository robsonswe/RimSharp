using System;
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
            if (value is bool boolValue && parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length < 2) return Brushes.Black; // Fallback

                try
                {
                    var colorName = boolValue ? parts[0] : parts[1];
                    
                    // First try to find the color in application resources
                    if (Application.Current.TryFindResource(colorName) is Brush resourceBrush)
                    {
                        return resourceBrush;
                    }
                    
                    // If not found in resources, try to parse as color name
                    if (ColorConverter.ConvertFromString(colorName) is Color color)
                    {
                        return new SolidColorBrush(color);
                    }
                }
                catch
                {
                    // If anything fails, use fallback
                }
            }
            
            // Fallback to RimworldBrownBrush or Black if all else fails
            return Application.Current.TryFindResource("RimworldBrownBrush") as Brush ?? Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}