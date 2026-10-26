// Features/WorkshopDownloader/Components/Browser/LinuxNativeWebView.cs
#nullable enable
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    /// <summary>
    /// NativeWebView with the app-side equivalent of upstream PR #38
    /// ("Fix Linux WebView first-paint and Wayland-session GTK init", issue #5).
    /// The fix is NOT contained in the released Avalonia.Controls.WebView 12.1.1
    /// (verified: NativeWebView.MeasureOverride is only the infinity-substitution
    /// stub and no ArrangeOverride exists in the 12.1.1 assembly).
    ///
    /// Root cause of the blank white render on X11: NativeWebView is a plain
    /// Control that adds its inner NativeControlHost to VisualChildren after
    /// async adapter creation, but never measures/arranges it. The host's Bounds
    /// stay 0x0, NativeControlHost.TryUpdateNativeControlPosition's
    /// "if (bounds.Width == 0 && bounds.Height == 0) return false" guard skips
    /// ShowInBounds, and the X11 holder window stays 1x1/unmapped - navigation
    /// completes "successfully" in an invisible window.
    ///
    /// Fix (mirrors PR #38): drive Measure/Arrange on the visual children and
    /// invalidate layout when the adapter appears/disappears so the freshly
    /// added child participates in the next layout pass.
    /// </summary>
    public class LinuxNativeWebView : NativeWebView
    {
        public LinuxNativeWebView()
        {
            // The inner host/control is added to VisualChildren inside the
            // adapter-created path; VisualChildren.Add does not invalidate
            // layout, so force a new measure/arrange pass (our handlers run
            // after NativeWebView's own because they are subscribed later).
            AdapterCreated += (_, _) => InvalidateMeasure();
            AdapterDestroyed += (_, _) => InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var result = base.MeasureOverride(availableSize);
            foreach (var child in VisualChildren.OfType<Layoutable>())
            {
                child.Measure(result);
            }
            return result;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in VisualChildren.OfType<Layoutable>())
            {
                child.Arrange(new Rect(finalSize));
            }
            return finalSize;
        }
    }
}
