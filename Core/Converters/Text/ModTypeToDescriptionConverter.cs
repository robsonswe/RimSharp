using System;
using System.Globalization;
using System.Windows.Data;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class ModTypeToDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ModType modType) return string.Empty;

            return modType switch
            {
                ModType.Core => "Core Game",
                ModType.Expansion => "Expansion/DLC",
                ModType.Workshop => "Workshop Mod",
                ModType.WorkshopL => "Workshop Mod (Local)",
                ModType.Git => "Git Repository",
                ModType.Zipped => "Local/Zipped Mod",
                _ => "Unknown Mod Type"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}