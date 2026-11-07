using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class ModTypeToDescriptionConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType type)
            {
                return type switch
                {
                    ModType.Core => "Core Game",
                    ModType.Expansion => "Expansion/DLC",
                    ModType.Workshop => "Workshop Mod",
                    ModType.Tracked => "Tracked for updates (downloaded copy)",
                    ModType.Git => "Git Repository",
                    ModType.Untracked => "Local copy (not tracked for updates)",
                    _ => "Unknown Mod Type"
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

