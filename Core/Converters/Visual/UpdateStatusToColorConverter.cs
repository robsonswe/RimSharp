using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RimSharp.Core.Converters.Visual
{
    public class UpdateStatusToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                string statusLower = status.ToLowerInvariant();
                string key;

                if (statusLower.Contains("up to date")) key = "RimworldDarkGreenBrush";
                else if (statusLower.Contains("update(s)") || statusLower.Contains("available") || statusLower.Contains("behind")) key = "RimworldHighlightBrush";
                else if (statusLower.Contains("error") || statusLower.Contains("fail") || statusLower.Contains("conflict")) key = "RimworldErrorRedBrush";
                else if (statusLower.Contains("checking") || statusLower.Contains("fetching")) key = "RimworldConfigOrangeBrush";
                else key = "RimworldGrayBrush";

                if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
                {
                    return resource as IBrush;
                }
            }
            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
