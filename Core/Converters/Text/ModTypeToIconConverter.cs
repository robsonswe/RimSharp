using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    /// <summary>
    /// Maps a ModType to the Font Awesome glyph shown in the mod list badge
    /// (replaces the old single-letter badges). Brand glyphs (Steam, Git) are
    /// part of Font Awesome Free and supported by the icon provider.
    /// </summary>
    public class ModTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModType type)
            {
                return type switch
                {
                    ModType.Core => "fa-earth-americas",
                    ModType.Expansion => "fa-puzzle-piece",
                    ModType.Workshop => "fa-steam",
                    ModType.Tracked => "fa-eye",
                    ModType.Git => "fa-git-alt",
                    ModType.Untracked => "fa-eye-slash",
                    _ => "fa-question"
                };
            }
            return "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
