using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Data.Converters;
using RimSharp.Core.Commands.Base;

namespace RimSharp.Core.Converters.Utility
{
    public class LinkToCommandConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string url)
            {
                return new RelayCommand(() =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"LinkToCommandConverter: Failed to open URL '{url}': {ex.Message}");
                    }
                });
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

