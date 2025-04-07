using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters
{
    public class ListToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is List<string> list && list.Count > 0)
        {
            string separator = parameter as string ?? ", ";
            return string.Join(separator, list);
        }
        return "None";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
}

