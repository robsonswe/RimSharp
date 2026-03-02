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

                return type switch
                {
                    MessageDialogType.Information => "fa-info",
                    MessageDialogType.Warning => "fa-triangle-exclamation",
                    MessageDialogType.Error => "fa-xmark",
                    MessageDialogType.Question => "fa-question",
                    _ => ""
                };            }
            return "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}


