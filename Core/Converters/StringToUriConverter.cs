using System;
using System.Globalization;
using System.Windows.Data;

namespace RimSharp.Core.Converters
{
    public class StringToUriConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string uriString && !string.IsNullOrWhiteSpace(uriString))
            {
                // Try to create a Uri. Use UriKind.Absolute or RelativeOrAbsolute as needed.
                // UriKind.RelativeOrAbsolute is often safer for flexibility.
                if (Uri.TryCreate(uriString, UriKind.Absolute, out Uri result))
                {
                    // Check if the scheme is something executable (http, https, ftp, mailto)
                    // to prevent potential security issues with local file paths if not intended.
                    if (result.IsWellFormedOriginalString() &&
                       (result.Scheme == Uri.UriSchemeHttp ||
                        result.Scheme == Uri.UriSchemeHttps ||
                        result.Scheme == Uri.UriSchemeFtp ||
                        result.Scheme == Uri.UriSchemeMailto)) // Add other schemes if needed
                    {
                         return result;
                    }
                    // Optional: Handle other potentially valid schemes like 'file' if necessary,
                    // but be cautious about opening local paths directly.
                    // else if (result.Scheme == Uri.UriSchemeFile) { return result; }
                }
            }
            // Return null if the input is null, empty, whitespace, or not a valid absolute URI with an allowed scheme.
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not usually needed for NavigateUri
            throw new NotImplementedException();
        }
    }
}
