using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Utility
{
    public class FilterButtonTagConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 3 && values[0] is string header)
            {
                bool active = values[1] is bool b1 && b1;
                bool inactive = values[2] is bool b2 && b2;

                if (header == "Active" && active) return "ActiveFiltered";
                if (header == "Inactive" && inactive) return "InactiveFiltered";
            }
            return "Unfiltered";
        }
    }
}
