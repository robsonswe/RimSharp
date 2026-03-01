using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Numeric
{
    public class MultiplyMultiValueConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            double result = 1.0;
            foreach (var value in values)
            {
                if (value is double d) result *= d;
                else if (value is int i) result *= i;
                else if (value is float f) result *= f;
            }

            if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
            {
                result *= p;
            }

            return result;
        }
    }
}
