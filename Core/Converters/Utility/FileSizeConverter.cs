using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters.Utility
{
    public class FileSizeConverter : IValueConverter
    {
        private const long OneKB = 1024;
        private const long OneMB = OneKB * 1024;
        private const long OneGB = OneMB * 1024;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                if (bytes < 0) return "N/A"; // Handle potentially invalid negative size
                if (bytes == 0) return "0 Bytes";

                if (bytes >= OneGB)
                    return $"{(double)bytes / OneGB:F2} GB"; // Two decimal places for GB
                if (bytes >= OneMB)
                    return $"{(double)bytes / OneMB:F1} MB"; // One decimal place for MB
                if (bytes >= OneKB)
                    return $"{(double)bytes / OneKB:F1} KB"; // One decimal place for KB

                return $"{bytes} Bytes";
            }
            return "N/A"; // Return N/A if input is not a long or is null
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("FileSizeConverter cannot convert back.");
        }
    }
}
