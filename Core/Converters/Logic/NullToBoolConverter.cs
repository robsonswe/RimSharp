using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters.Logic
{
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value is bool? && ((bool?)value).HasValue == false)
            {
                return parameter?.ToString() == "Null";
            }
            
            if (value is bool boolValue)
            {
                if (boolValue)
                {
                    return parameter?.ToString() == "True";
                }
                else
                {
                    return parameter?.ToString() == "False";
                }
            }
            
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool)value)
            {
                switch (parameter?.ToString())
                {
                    case "Null": return null;
                    case "True": return true;
                    case "False": return false;
                }
            }
            return null;
        }
    }
}