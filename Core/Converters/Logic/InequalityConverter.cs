using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Logic
{
    public class InequalityConverter : IMultiValueConverter, IValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2) return true;
            
            var val1 = values[0];
            var val2 = values[1];

            if (val1 == null && val2 == null) return false;
            if (val1 == null || val2 == null) return true;

            if (val1.GetType() == val2.GetType())
            {
                return !object.Equals(val1, val2);
            }

            // Different types, compare as strings
            return val1.ToString() != val2.ToString();
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null && parameter == null) return false;
            if (value == null || parameter == null) return true;

            return value.ToString() != parameter.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && !boolValue)
            {
                return parameter;
            }
            return null;
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            return new object?[] { null, null };
        }
    }
}
