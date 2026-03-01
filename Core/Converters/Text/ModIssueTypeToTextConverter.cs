using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class ModIssueTypeToTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ModIssueType type)
            {
                return type switch
                {
                    ModIssueType.Sorting => "Sorting Order",
                    ModIssueType.Dependency => "Missing Dependency",
                    ModIssueType.Incompatibility => "Incompatibility",
                    ModIssueType.SoftIncompatibility => "Soft Incompatibility",
                    ModIssueType.HardIncompatibility => "Hard Incompatibility",
                    ModIssueType.VersionMismatch => "Version Mismatch",
                    ModIssueType.Duplicate => "Duplicate Mod",
                    _ => type.ToString()
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
