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

            Current.Resources["BrushStateDanger"] = Brushes.Red;
            Current.Resources["BrushStateSuccess"] = Brushes.DarkGreen;
            Current.Resources["BrushStatePrimary"] = Brushes.Brown;
            Current.Resources["BrushStateHighlight"] = Brushes.Yellow;
            Current.Resources["BrushStatePanel"] = Brushes.Beige;
            Current.Resources["BrushStateNeutral"] = Brushes.Gray;
            Current.Resources["BrushTextBlack"] = Brushes.Black;
            Current.Resources["BrushTextOnDark"] = Brushes.White;
            Current.Resources["BrushStateWarning"] = Brushes.Orange;
            Current.Resources["BrushStateDangerStrong"] = Brushes.Red;
            Current.Resources["BrushTextMuted"] = Brushes.SandyBrown;
        }
    }
}

