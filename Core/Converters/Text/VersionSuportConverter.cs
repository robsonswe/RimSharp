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
            // Check if the value is a List<VersionSupport> and if it's not null or empty
            if (value is not List<VersionSupport> versions || !versions.Any())
            {
                // Return a simple Run if there are no versions
                return new Run("None");
            }

            // Create a TextBlock to hold the formatted version strings
            var textBlock = new TextBlock();
            textBlock.TextWrapping = TextWrapping.Wrap; // Allow wrapping if needed

            // Define Brushes and Styles (lookup once for efficiency)
            var unofficialBrush = Application.Current.TryFindResource("RimworldLightBrownBrush") as SolidColorBrush
                                  ?? Brushes.SandyBrown; // Fallback color
            var toolTipStyle = Application.Current.TryFindResource("RimworldToolTip") as Style;


            // Iterate through the list of supported versions
            for (int i = 0; i < versions.Count; i++)
            {
                var versionSupport = versions[i];
                // Create a Run element for the version string
                var run = new Run(versionSupport.Version);

                // Check if the source is NOT official to apply special styling and tooltips
                if (versionSupport.Source != VersionSource.Official)
                {
                    // Apply visual styling for non-official versions
                    run.Foreground = unofficialBrush;
                    run.FontStyle = FontStyles.Italic;

                    // Determine the tooltip content based on the source
                    string tooltipContent = versionSupport.Source switch
                    {
                        VersionSource.Mlie => "Unofficial: Compatibility added via Mlie's 'No Version Warning' Mod.",
                        VersionSource.Database => "Unofficial: Compatibility added via app's rules database.",
                        VersionSource.Custom => "Unofficial: Compatibility added via your custom settings.",
                        _ => "Unofficial: Compatibility source unknown."
                    };

                    // Create and assign the tooltip only if the style is found
                    if (toolTipStyle != null)
                    {
                        run.ToolTip = new ToolTip
                        {
                            Content = tooltipContent,
                            Style = toolTipStyle
                        };
                    }
                    else
                    {
                        // Basic tooltip if style is missing
                        run.ToolTip = tooltipContent;
                    }
                }
                // else: Source is Official, no special style or tooltip needed

                // Add the formatted Run to the TextBlock
                textBlock.Inlines.Add(run);

                // Add a separator (comma and space) if it's not the last version in the list
                if (i < versions.Count - 1)
                {
                    textBlock.Inlines.Add(new Run(", "));
                }
            }

            // Return the fully constructed TextBlock
            return textBlock;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This converter is one-way (Model -> View)
            throw new NotImplementedException("VersionSupportConverter cannot convert back.");
        }
    }
}