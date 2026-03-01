using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Visual
{
    public class BooleanToThicknessConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && parameter is string values)
            {
                var parts = values.Split('|');
                if (parts.Length == 2)
                {
                    try
                    {
                        string thicknessStr = b ? parts[0] : parts[1];
                        return Thickness.Parse(thicknessStr);
                    }
                    catch
                    {
                        return new Thickness(0);
                    }
                }
            }
            return new Thickness(0);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

