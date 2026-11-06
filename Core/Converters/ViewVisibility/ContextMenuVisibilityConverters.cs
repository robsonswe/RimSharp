using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.ViewVisibility
{
    /// <summary>

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

    /// </summary>
    public class ModTypeToRedownloadVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType modType)
            {
                return modType == ModType.Tracked;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>

    /// Tracked, Git, and Untracked mods can be deleted
    /// </summary>
    public class ModTypeToDeleteVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType modType)
            {
                // Hide for Core, Expansion, and Workshop (not Tracked)
                return modType != ModType.Core && modType != ModType.Expansion && modType != ModType.Workshop;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether a mod can be managed via Steam Workshop actions.
    /// </summary>
    public class WorkshopModVisibilityConverter : IMultiValueConverter, IValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count == 0) return false;

            var first = values[0];
            ModType? modType = first as ModType?;
            if (modType == null && first is int intVal) modType = (ModType)intVal;
            return modType == ModType.Workshop;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModItem mod) return mod.IsWorkshopMod;
            if (value is ModType modType) return modType == ModType.Workshop;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true for Tracked mods (SteamCMD-downloaded mods)
    /// </summary>
    public class ModTypeToTrackedConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType modType)
            {
                return modType == ModType.Tracked;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}


