using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RimSharp.Core.Converters.Visual
{
    public class DarkenColorConverter : IValueConverter
    {
        public double DarkenFactor { get; set; } = 0.7;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Color color) return Darken(color);
            if (value is ISolidColorBrush brush) return new SolidColorBrush(Darken(brush.Color));
            return value;
        }

        private Color Darken(Color color)
        {
            return Color.FromArgb(color.A, (byte)(color.R * DarkenFactor), (byte)(color.G * DarkenFactor), (byte)(color.B * DarkenFactor));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

