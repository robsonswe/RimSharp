using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class VersionSupportConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not List<VersionSupport> versions || !versions.Any())
                return new Run("None");

            var textBlock = new TextBlock();

            for (int i = 0; i < versions.Count; i++)
            {
                var version = versions[i];
                var run = new Run(version.Version);
                
                // Use different styling for unofficial versions
                if (version.Unofficial)
                {
                    run.Foreground = (SolidColorBrush)Application.Current.FindResource("RimworldLightBrownBrush");
                    run.FontStyle = FontStyles.Italic;
                    
                    // Create styled tooltip
                    var toolTip = new ToolTip
                    {
                        Content = "Reported working, but no official support for this version.",
                        Style = (Style)Application.Current.FindResource("RimworldToolTip")
                    };
                    run.ToolTip = toolTip;
                }

                textBlock.Inlines.Add(run);

                // Add separator if not the last item
                if (i < versions.Count - 1)
                {
                    textBlock.Inlines.Add(new Run(", "));
                }
            }

            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}