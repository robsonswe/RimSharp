using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters.Numeric
{
    public class MultiplyMultiValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) 
                return 0;

            double result = 1;
            foreach (var value in values)
            {
                if (value is double d)
                    result *= d;
                else if (value is int i)
                    result *= i;
                else if (value is float f)
                    result *= f;
            }

            // Apply parameter as multiplier if provided
            if (parameter is string paramStr && double.TryParse(paramStr, out double param))
                result *= param;
            
            return result;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}