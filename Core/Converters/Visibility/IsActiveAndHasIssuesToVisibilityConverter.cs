using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace RimSharp.Core.Converters.ViewVisibility
{
    public class IsActiveAndHasIssuesToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expecting two boolean values: [0] = HasIssues, [1] = IsActive
            if (values == null || values.Length < 2)
            {
                return Visibility.Collapsed; // Not enough data
            }

            // Safely try to convert values to boolean
            bool hasIssues = false;
            if (values[0] is bool b0)
            {
                hasIssues = b0;
            }
            else if (values[0] != null && values[0] != DependencyProperty.UnsetValue)
            {
                 // Log or handle unexpected type if necessary
                 System.Diagnostics.Debug.WriteLine($"[IsActiveAndHasIssuesToVisibilityConverter] Warning: Expected bool for HasIssues, got {values[0].GetType()}");
                 return Visibility.Collapsed;
            }


            bool isActive = false;
            if (values[1] is bool b1)
            {
                isActive = b1;
            }
             else if (values[1] != null && values[1] != DependencyProperty.UnsetValue)
            {
                 // Log or handle unexpected type if necessary
                 System.Diagnostics.Debug.WriteLine($"[IsActiveAndHasIssuesToVisibilityConverter] Warning: Expected bool for IsActive, got {values[1].GetType()}");
                 return Visibility.Collapsed;
            }

            // Return Visible only if BOTH are true
            return (hasIssues && isActive) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // Not needed for one-way binding to Visibility
            throw new NotImplementedException();
        }
    }
}
