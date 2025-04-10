using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RimSharp.Core.Converters
{
    public class DarkenColorConverter : IValueConverter
    {
        public double DarkenFactor { get; set; } = 0.7; // Adjust this value (0-1) for desired darkness

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return DarkenColor(color, DarkenFactor);
            }
            else if (value is SolidColorBrush brush)
            {
                return new SolidColorBrush(DarkenColor(brush.Color, DarkenFactor));
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private Color DarkenColor(Color color, double factor)
        {
            return Color.FromArgb(
                color.A,
                (byte)(color.R * factor),
                (byte)(color.G * factor),
                (byte)(color.B * factor));
        }
    }
}