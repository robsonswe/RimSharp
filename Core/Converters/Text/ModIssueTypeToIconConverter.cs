using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class ModIssueTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModIssueType type)
            {
                return type switch
                {
                    ModIssueType.Sorting => "🔃",
                    ModIssueType.Dependency => "📦",
                    ModIssueType.Incompatibility => "🚫",
                    ModIssueType.VersionMismatch => "⚠️",
                    ModIssueType.Duplicate => "👯",
                    _ => "❓"
                };
            }
            return "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
