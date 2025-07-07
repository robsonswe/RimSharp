using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RimSharp.Core.Converters.Text
{
    public class TrimmedTextTooltipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expecting two values: the TextBlock itself and its ActualWidth
            if (values.Length < 2 || !(values[0] is TextBlock textBlock) || !(values[1] is double actualWidth))
            {
                return null;
            }

            // Create a FormattedText object to measure the desired width of the text
            var formattedText = new FormattedText(
                textBlock.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                textBlock.FontSize,
                textBlock.Foreground,
                new NumberSubstitution(),
                TextFormattingMode.Display
            );

            // If the desired width is greater than the available width, the text is trimmed.
            // In this case, return the full text to be used as a tooltip.
            if (formattedText.Width > actualWidth)
            {
                return textBlock.Text;
            }

            // Otherwise, the text is not trimmed, so no tooltip is needed.
            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}