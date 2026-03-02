using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Layout;
using RimSharp.Shared.Models;

namespace RimSharp.Core.Converters.Text
{
    public class VersionSupportConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not List<VersionSupport> versions || !versions.Any())
            {
                if (targetType == typeof(string)) return "None";
                return CreateDefaultTextBlock("None");
            }

            var versionsToDisplay = versions.AsEnumerable();
            if (parameter is string paramString && paramString == "OfficialOnly")
            {
                versionsToDisplay = versions.Where(v => v.Source == VersionSource.Official);
            }

            var displayList = versionsToDisplay.ToList();
            if (!displayList.Any()) 
            {
                if (targetType == typeof(string)) return "None";
                return CreateDefaultTextBlock("None");
            }

            if (targetType == typeof(string))
            {
                return string.Join(", ", displayList.Select(v => v.Version));
            }

            // Otherwise return a rich UI representation
            var panel = new WrapPanel 
            { 
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            IBrush? unofficialBrush = null;
            IBrush? defaultBrush = null;

            if (Application.Current != null)
            {
                Application.Current.TryGetResource("RimworldLightBrownBrush", out var unofficialObj);
                unofficialBrush = unofficialObj as IBrush;

                Application.Current.TryGetResource("RimworldBrownBrush", out var defaultObj);
                defaultBrush = defaultObj as IBrush;
            }

            unofficialBrush ??= Brushes.SandyBrown;
            defaultBrush ??= Brushes.SaddleBrown;

            for (int i = 0; i < displayList.Count; i++)
            {
                var versionSupport = displayList[i];
                var versionText = versionSupport.Version;
                if (i < displayList.Count - 1) versionText += ", ";

                var textBlock = new TextBlock 
                { 
                    Text = versionText,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Foreground = defaultBrush
                };

                if (versionSupport.Source != VersionSource.Official)
                {
                    textBlock.Foreground = unofficialBrush;
                    textBlock.FontStyle = FontStyle.Italic;

                    string tooltipContent = versionSupport.Source switch
                    {
                        VersionSource.Mlie => "Unofficial: Compatibility added via Mlie's 'No Version Warning' Mod.",
                        VersionSource.Database => "Unofficial: Compatibility added via app's rules database.",
                        VersionSource.Custom => "Unofficial: Compatibility added via your custom settings.",
                        _ => "Unofficial: Compatibility source unknown."
                    };
                    ToolTip.SetTip(textBlock, tooltipContent);
                }

                panel.Children.Add(textBlock);
            }

            return panel;
        }

        private TextBlock CreateDefaultTextBlock(string text)
        {
            IBrush? defaultBrush = null;
            if (Application.Current != null)
            {
                Application.Current.TryGetResource("RimworldBrownBrush", out var defaultObj);
                defaultBrush = defaultObj as IBrush;
            }
            defaultBrush ??= Brushes.SaddleBrown;

            return new TextBlock 
            { 
                Text = text, 
                FontSize = 12, 
                Foreground = defaultBrush,
                VerticalAlignment = VerticalAlignment.Center 
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}


