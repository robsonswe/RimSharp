using System;
using System.Globalization;
using System.Windows.Data;
using System.Diagnostics;

namespace RimSharp.Core.Converters.Text
{
    /// <summary>
    /// Converts a boolean value to one of two specified text strings.
    /// Expects the ConverterParameter to be a string in the format "TextForFalse|TextForTrue".
    /// Falls back to default strings if the parameter is invalid.
    /// </summary>
    public class ConditionalTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Default fallback texts
            string textForFalse = "State False";
            string textForTrue = "State True";

            // Parse the parameter
            if (parameter is string paramString && !string.IsNullOrEmpty(paramString))
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    textForFalse = parts[0];
                    textForTrue = parts[1];
                }
                else
                {
                    Debug.WriteLine($"[ConditionalTextConverter] Invalid parameter format: '{paramString}'. Expected 'TextForFalse|TextForTrue'. Using fallback texts.");
                }
            }
            else
            {
                 Debug.WriteLine($"[ConditionalTextConverter] Parameter is null or empty. Using fallback texts.");
            }

            // Determine output based on input value
            if (value is bool condition)
            {
                string resultText = condition ? textForTrue : textForFalse;
                // Debug.WriteLine($"[ConditionalTextConverter] Input: {condition}, Param: '{parameter}', Output: '{resultText}'");
                return resultText;
            }

            Debug.WriteLine($"[ConditionalTextConverter] Input value is not a bool ({value}), returning text for false state: '{textForFalse}'");
            return textForFalse; // Fallback for non-boolean input
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not needed for one-way binding
            throw new NotImplementedException();
        }
    }
}
