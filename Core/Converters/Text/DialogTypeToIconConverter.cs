using System;
using System.Globalization;
using Avalonia.Data.Converters;
using RimSharp.AppDir.Dialogs;

namespace RimSharp.Core.Converters.Text
{
    public class DialogTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is MessageDialogType type)
            {
                // Using simple characters that look subtle when styled with Segoe UI Symbol
                return type switch
                {
                    MessageDialogType.Information => "i",
                    MessageDialogType.Warning => "!",
                    MessageDialogType.Error => "X",
                    MessageDialogType.Question => "?",
                    _ => ""
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

