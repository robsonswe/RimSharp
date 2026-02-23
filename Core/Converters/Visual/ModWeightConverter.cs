using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Visual
{
    public class ModWeightConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isBold = false;
            
            if (values.Count >= 1 && values[0] is bool favorite && favorite)
            {
                isBold = true;
            }
            
            if (!isBold && values.Count >= 2 && values[1] is ModType type)
            {
                if (type == ModType.Core || type == ModType.Expansion)
                {
                    isBold = true;
                }
            }

            return isBold ? FontWeight.Bold : FontWeight.Normal;
        }
    }
}
