using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Logic
{
    public class EqualityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null && parameter == null) return true;
            if (value == null || parameter == null) return false;
            
            return value.ToString() == parameter.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return parameter;
            }
            return null;
        }
    }
}
