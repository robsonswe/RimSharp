using System;
using System.Globalization;
using System.Windows.Data;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class ModTypeToInitialConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ModType modType) return string.Empty;

            return modType switch
            {
                ModType.Core => "C",
                ModType.Expansion => "E",
                ModType.Workshop => "W",
                ModType.WorkshopL => "WL",
                ModType.Git => "G",
                ModType.Zipped => "Z",
                _ => string.Empty
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
