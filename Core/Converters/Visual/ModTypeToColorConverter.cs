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
                    ModType.Core => "BrushStateDanger",
                    ModType.Expansion => "BrushStateSuccess",
                    ModType.Workshop => "BrushStatePrimary",
                    ModType.WorkshopL => "BrushStateHighlight",
                    ModType.Git => "BrushStatePanel",
                    ModType.Zipped => "BrushStateNeutral",
                    _ => "BrushStatePrimary"
                };

                if (Application.Current?.Resources.TryGetResource(colorKey, null, out var resource) == true)
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
