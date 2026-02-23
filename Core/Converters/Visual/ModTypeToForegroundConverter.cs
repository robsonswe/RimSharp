using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Visual
{
    public class ModTypeToForegroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType type && type == ModType.WorkshopL)
            {
                if (Application.Current?.Resources.TryGetResource("RimworldBlackBrush", null, out var resource) == true)
                {
                    return resource as IBrush;
                }
                return Brushes.Black;
            }
            
            if (Application.Current?.Resources.TryGetResource("RimworldWhiteBrush", null, out var white) == true)
            {
                return white as IBrush;
            }
            return Brushes.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

