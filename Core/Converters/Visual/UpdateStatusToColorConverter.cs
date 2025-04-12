using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows;
using System.Drawing;

namespace RimSharp.Core.Converters.Visual
{
    public class UpdateStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                switch (status.ToLower())
                {
                    case "up to date":
                        return (Brush)Application.Current.TryFindResource("RimworldGreenBrush") 
                            ?? Brushes.Green;
                    case var s when s.Contains("new updates"):
                        return (Brush)Application.Current.TryFindResource("RimworldYellowBrush") 
                            ?? Brushes.Yellow;
                    case "error checking updates":
                        return (Brush)Application.Current.TryFindResource("RimworldRedBrush") 
                            ?? Brushes.Red;
                    case "no tracking branch":
                        return (Brush)Application.Current.TryFindResource("RimworldLightBrownBrush") 
                            ?? Brushes.LightGray;
                    default:
                        return (Brush)Application.Current.TryFindResource("RimworldBrownBrush") 
                            ?? Brushes.Gray;
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}