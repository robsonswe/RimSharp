using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters.Utility
{
    public class FilterButtonTagConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 && values[0] is string headerText)
            {
                bool isActive = values[1] as bool? ?? false;
                bool isInactive = values[2] as bool? ?? false;

                if (headerText == "Active" && isActive) return "ActiveFiltered";
                if (headerText == "Inactive" && isInactive) return "InactiveFiltered";
            }
            return "Unfiltered";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}