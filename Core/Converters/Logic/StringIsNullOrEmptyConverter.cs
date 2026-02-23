using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Logic
{
    public class StringIsNullOrEmptyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return string.IsNullOrEmpty(str);
            }
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

