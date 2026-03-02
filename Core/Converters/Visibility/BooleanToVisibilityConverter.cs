using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.ViewVisibility
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>

        /// (true -> false, false -> true).
        /// </summary>
        public bool Inverse { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = Inverse;
                if (parameter is bool b) invert = b;
                else if (parameter is string s && bool.TryParse(s, out var bParsed)) invert = bParsed;
                else if (parameter != null && parameter.ToString() == "Invert") invert = true;

                bool result = invert ? !boolValue : boolValue;
                return result;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = Inverse;
                if (parameter is bool b) invert = b;
                else if (parameter is string s && bool.TryParse(s, out var bParsed)) invert = bParsed;
                else if (parameter != null && parameter.ToString() == "Invert") invert = true;

                return invert ? !boolValue : boolValue;
            }
            return false;
        }
    }
}

