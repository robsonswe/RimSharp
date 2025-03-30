using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Views.Converters
{
    public class InequalityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expecting two values: [0] = SelectedTab, [1] = CommandParameter
            if (values == null || values.Length < 2)
            {
                return true; // Not enough info, assume not equal (enabled)
            }

            var val1 = values[0];
            var val2 = values[1];

            // If one is null and the other isn't, they are not equal
            if ((val1 == null) != (val2 == null)) // Using XOR for concise null check
            {
                return true; // Not equal -> Enabled
            }

            // If both are null, they are equal
            if (val1 == null && val2 == null)
            {
                return false; // Equal -> Disabled
            }

            // Compare as strings for robustness, matching CommandParameter usage
            return val1.ToString() != val2.ToString(); // True if NOT equal (Enabled)
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // Not needed for one-way binding to IsEnabled
            throw new NotImplementedException();
        }
    }
}
