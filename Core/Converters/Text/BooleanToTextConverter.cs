using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Text
{
    public class BooleanToTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string texts)
            {
                var parts = texts.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[1] : parts[0];
                }
            }
            return "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string text && parameter is string texts)
            {
                var parts = texts.Split('|');
                if (parts.Length == 2)
                {
                    if (string.Equals(text, parts[1], StringComparison.Ordinal)) return true;
                    if (string.Equals(text, parts[0], StringComparison.Ordinal)) return false;
                }
            }
            return null;
        }
    }
}
