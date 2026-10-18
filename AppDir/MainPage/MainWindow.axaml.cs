using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RimSharp.AppDir.MainPage
{
    public partial class MainWindow : Window
    {
        // Design-time values (XAML fallbacks). On screens large enough to fit
        // them (typical Windows desktops) these are the effective values, so
        // Windows behavior is unchanged; smaller screens get fitted values.
        private const double DesignMinWidth = 1200;
        private const double DesignMinHeight = 700;
        private const double DesignWidth = 1200;
        private const double DesignHeight = 900;

        public MainWindow()
        {
            InitializeComponent();
            // Screens IS available in the constructor (PlatformImpl is created
            // in Window's ctor). Setting Min* here means the first
            // WM_NORMAL_HINTS written before mapping already has the correct
            // values — xfwm4 reads size hints at map time, so this is the
            // bulletproof timing (no flicker, no post-map hint race).
            FitToWorkArea();

            // Workaround for Avalonia X11 bug (AvaloniaUI/Avalonia#8869, #2523):
            // WindowState set in XAML is applied before the window is mapped and
            // ignored by some WMs (e.g. xfwm4). Applying it after Opened goes
            // through the _NET_WM_STATE client-message path, which works.
            // Windows behavior is unchanged (setting it again after Opened is a no-op there).
            Opened += (_, _) =>
            {
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            };
        }

        private void FitToWorkArea()
        {
            // Window not yet positioned -> resolve like CenterScreen would:
            // primary screen. (After show/move, Screens.ScreenFromWindow(this)
            // would be the correct call — largest-intersection semantics.)
            var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
            if (screen is null)
                return; // headless/edge case; XAML defaults apply

            // Screen.WorkingArea is in DEVICE pixels (PixelRect);
            // Window.Width/Height/Min* are DIPs -> divide by Screen.Scaling.
            var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var workArea = screen.WorkingArea;
            double maxW = workArea.Width / scale;
            double maxH = workArea.Height / scale;

            // Minimums: capped at design values, shrunk to fit smaller workareas.
            MinWidth = Math.Min(DesignMinWidth, maxW);
            MinHeight = Math.Min(DesignMinHeight, maxH);

            // Initial size: clamped to fit the workarea (design size on large
            // screens). The user can still resize freely afterwards.
            Width = Math.Min(DesignWidth, maxW);
            Height = Math.Min(DesignHeight, maxH);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
