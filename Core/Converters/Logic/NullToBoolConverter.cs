using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters.Logic
{
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This part is likely correct - determines if THIS radio button should be checked
            string paramString = parameter?.ToString();

            if (value == null) // ViewModel property is null
            {
                return paramString == "Null"; // Check the "Any" button
            }
            else if (value is bool boolValue) // ViewModel property is true or false
            {
                if (boolValue) // ViewModel property is true
                {
                    return paramString == "True"; // Check the "Yes" button
                }
                else // ViewModel property is false
                {
                    return paramString == "False"; // Check the "No" button
                }
            }
            
            // Fallback, should ideally not be reached for bool?
            return false; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This is called when a RadioButton's IsChecked changes.
            // 'value' is the new IsChecked state (true or false).
            // 'parameter' is the value this specific RadioButton represents ("Null", "True", "False").

            // Only update the source property if THIS RadioButton is being CHECKED (value is true)
            if (value is bool isChecked && isChecked)
            {
                string paramString = parameter?.ToString();
                switch (paramString)
                {
                    case "Null": return null;  // Set ViewModel property to null
                    case "True": return true;   // Set ViewModel property to true
                    case "False": return false;  // Set ViewModel property to false
                }
            }

            return Binding.DoNothing;
        }
    }
}