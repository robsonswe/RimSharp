using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RimSharp.Core.Converters.ViewVisibility
{
    public class IsNotNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public bool Inverse { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = value != null && 
                            !string.IsNullOrEmpty(value.ToString());

            if (Inverse)
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}