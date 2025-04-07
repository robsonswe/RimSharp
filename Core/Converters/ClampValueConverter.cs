using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters
{
    public class ClampValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue && parameter is string paramString)
            {
                string[] limits = paramString.Split(',');
                if (limits.Length == 2 && 
                    double.TryParse(limits[0], out double minValue) && 
                    double.TryParse(limits[1], out double maxValue))
                {
                    // Scale the value proportionally between min and max based on available space
                    double scale = doubleValue / 1000; // Adjust scaling factor as needed
                    double scaledValue = minValue + (maxValue - minValue) * scale;
                    
                    // Clamp to min/max range
                    return Math.Min(Math.Max(scaledValue, minValue), maxValue);
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}