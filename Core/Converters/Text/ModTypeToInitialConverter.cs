using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class ModTypeToInitialConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType type)
            {
                return type switch
                {
                    ModType.Core => "C",
                    ModType.Expansion => "E",
                    ModType.Workshop => "W",
                    ModType.WorkshopL => "WL",
                    ModType.Git => "G",
                    ModType.Zipped => "Z",
                    _ => ""
                };
            }
            return "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

