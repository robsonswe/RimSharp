using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimSharp.Core.Converters.Utility
{
    public class FileSizeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            long bytes = 0;
            if (value is long l) bytes = l;
            else if (value is int i) bytes = i;
            else if (value is double d) bytes = (long)d;
            else return "N/A";

            if (bytes < 0) return "N/A";
            if (bytes == 0) return "Not Calculated";

            string[] units = { "Bytes", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            double size = bytes;
            
            if (size < 1024)
            {
                return $"{size} Bytes";
            }

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            // Using InvariantCulture to ensure dot as decimal separator
            // and matching the format expected by tests (1.0 KB, 1.0 MB, but 1.00 GB for some reason?)
            // Let's re-read the test expectations.
            
            if (unitIndex >= 3) // GB or larger
                return size.ToString("0.00", CultureInfo.InvariantCulture) + " " + units[unitIndex];
            
            return size.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unitIndex];
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("FileSizeConverter cannot convert back.");
        }
    }
}
