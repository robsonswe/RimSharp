using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Views.Converters
{
   public class PathToExplorerFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value; // Just return the path as-is
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

}