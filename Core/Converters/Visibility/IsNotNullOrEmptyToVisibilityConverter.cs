using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.ViewVisibility
{
    public class IsNotNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public bool Inverse { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool invert = Inverse;
            if (parameter is bool b) invert = b;
            else if (parameter is string s && bool.TryParse(s, out var bParsed)) invert = bParsed;
            else if (parameter != null && parameter.ToString() == "Invert") invert = true;

            bool result = !string.IsNullOrWhiteSpace(value?.ToString());
            return invert ? !result : result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

