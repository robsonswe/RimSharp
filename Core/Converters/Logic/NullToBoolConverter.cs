using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Logic
{
    public class NullToBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string param = parameter?.ToString() ?? "";
            bool? boolValue = value as bool?;

            if (param == "True") return boolValue == true;
            if (param == "False") return boolValue == false;
            if (param == "Null") return boolValue == null;

            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true)
            {
                string param = parameter?.ToString() ?? "";
                if (param == "True") return true;
                if (param == "False") return false;
                if (param == "Null") return null;
            }
            return AvaloniaProperty.UnsetValue;
        }
    }
}
