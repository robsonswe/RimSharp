using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.ViewVisibility
{
    public class IsActiveAndHasIssuesToVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 && values[0] is bool hasIssues && values[1] is bool isActive)
            {
                return hasIssues && isActive;
            }
            return false;
        }
    }
}
