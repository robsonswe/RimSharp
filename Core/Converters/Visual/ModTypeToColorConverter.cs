using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Visual
{
    public class ModTypeToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType type)
            {
                string colorKey = type switch
                {
                    ModType.Core => "RimworldRed",
                    ModType.Expansion => "RimworldDarkGreen",
                    ModType.Workshop => "RimworldBrown",
                    ModType.WorkshopL => "RimworldHighlight",
                    ModType.Git => "RimworldDarkBeige",
                    ModType.Zipped => "RimworldGray",
                    _ => "RimworldBrown"
                };

                if (Application.Current?.Resources.TryGetResource(colorKey + "Brush", null, out var resource) == true)
                {
                    return resource as IBrush;
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

