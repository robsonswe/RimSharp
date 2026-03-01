using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.ViewVisibility
{
    /// <summary>
    /// Hides context menu for Core and Expansion mods (they can't be modified/deleted)
    /// </summary>
    public class ContextMenuVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 0) return true;
            
            var modType = values[0] as ModType?;
            if (modType == null) return true;
            
            // Hide context menu for Core and Expansion mods
            return modType != ModType.Core && modType != ModType.Expansion;
        }
    }

    /// <summary>
    /// Shows Redownload option only for WorkshopL (locally installed Workshop mods)
    /// </summary>
    public class ModTypeToRedownloadVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType modType)
            {
                return modType == ModType.WorkshopL;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows Delete option for all mods EXCEPT Core, Expansion, and Workshop (not WorkshopL)
    /// WorkshopL, Git, and Zipped mods can be deleted
    /// </summary>
    public class ModTypeToDeleteVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType modType)
            {
                // Hide for Core, Expansion, and Workshop (not WorkshopL)
                return modType != ModType.Core && modType != ModType.Expansion && modType != ModType.Workshop;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

