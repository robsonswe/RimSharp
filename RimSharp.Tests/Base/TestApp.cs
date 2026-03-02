using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace RimSharp.Tests.Base
{
    public class TestApp : Application
    {
        public static void InitializeTestApp()
        {
            if (Current == null)
            {
                AppBuilder.Configure<TestApp>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
            }

            SetupResources();
        }

        private static void SetupResources()
        {
            if (Current == null) return;

            Current.Resources["RimworldRedBrush"] = Brushes.Red;
            Current.Resources["RimworldDarkGreenBrush"] = Brushes.DarkGreen;
            Current.Resources["RimworldBrownBrush"] = Brushes.Brown;
            Current.Resources["RimworldHighlightBrush"] = Brushes.Yellow;
            Current.Resources["RimworldDarkBeigeBrush"] = Brushes.Beige;
            Current.Resources["RimworldGrayBrush"] = Brushes.Gray;
            Current.Resources["RimworldBlackBrush"] = Brushes.Black;
            Current.Resources["RimworldWhiteBrush"] = Brushes.White;
            Current.Resources["RimworldConfigOrangeBrush"] = Brushes.Orange;
            Current.Resources["RimworldErrorRedBrush"] = Brushes.Red;
            Current.Resources["RimworldLightBrownBrush"] = Brushes.SandyBrown;
        }
    }
}

