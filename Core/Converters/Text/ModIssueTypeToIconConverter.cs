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
                    ModIssueType.Sorting => "fa-arrow-down-z-a",
                    ModIssueType.Dependency => "fa-box",
                    ModIssueType.Incompatibility => "fa-ban",
                    ModIssueType.SoftIncompatibility => "fa-triangle-exclamation",
                    ModIssueType.HardIncompatibility => "fa-ban",
                    ModIssueType.VersionMismatch => "fa-triangle-exclamation",
                    ModIssueType.Duplicate => "fa-clone",
                    _ => "fa-question"
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
