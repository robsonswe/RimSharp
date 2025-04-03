using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Diagnostics;
using System;

namespace RimSharp.Views.Modules.Mods.DragAdorner
{
    public class InsertionAdorner : Adorner
    {
        private readonly bool _isHorizontal; // True for ListBox items
        private readonly bool _isAbove;      // True to draw line above the element's top
        private readonly Pen _pen;
        private readonly UIElement _relativeToElement; // The element we position relative to

        public InsertionAdorner(UIElement adornedElement, UIElement relativeToElement,
                              bool isHorizontal, Brush brush, double thickness, bool isAbove)
            : base(adornedElement) // Adorns the ListBox usually
        {
            this._relativeToElement = relativeToElement;
            this._isHorizontal = isHorizontal;
            this._isAbove = isAbove;
            this._pen = new Pen(brush, thickness) { DashStyle = DashStyles.Solid }; // Ensure solid line
            this.IsHitTestVisible = false; // Don't interfere with mouse events
             // Adorner gets added by the Behavior/caller, not here
             Debug.WriteLine($"InsertionAdorner created. RelativeTo: {relativeToElement?.GetType().Name}, IsAbove: {isAbove}");
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_relativeToElement == null) return;

            try
            {
                // Calculate position relative to the AdornedElement (the ListBox)
                Point relativeStartPoint;
                Size relativeSize;

                // Check if relativeToElement is the same as AdornedElement (e.g., empty list)
                if (ReferenceEquals(_relativeToElement, AdornedElement))
                {
                     // Draw at top or bottom of the adorned element itself
                    relativeStartPoint = new Point(0, _isAbove ? 0 : AdornedElement.RenderSize.Height);
                    relativeSize = AdornedElement.RenderSize; // Use adorned element's size
                }
                else
                {
                    // Transform the top-left corner of the relative element to the adorned element's space
                    relativeStartPoint = _relativeToElement.TranslatePoint(new Point(0, 0), AdornedElement);
                    relativeSize = _relativeToElement.RenderSize;
                }


                double lineY;
                if (_isAbove)
                {
                    lineY = relativeStartPoint.Y - (_pen.Thickness / 2); // Center line slightly above top edge
                }
                else
                {
                     // Position below the element's bottom edge
                     lineY = relativeStartPoint.Y + relativeSize.Height + (_pen.Thickness / 2); // Center line slightly below bottom edge
                }

                // Clamp Y position within the adorned element's bounds to prevent drawing outside
                lineY = System.Math.Clamp(lineY, 0, AdornedElement.RenderSize.Height);

                // Draw horizontal line across the adorned element width
                if (_isHorizontal)
                {
                    // Start slightly indented for better visuals (optional)
                     double startX = 5;
                     double endX = AdornedElement.RenderSize.Width - 5;
                    // Ensure startX is not greater than endX
                     if (startX > endX) startX = endX;

                    drawingContext.DrawLine(_pen, new Point(startX, lineY), new Point(endX, lineY));
                     // Debug.WriteLine($"Drawing Insertion Line: Y={lineY}, Width={endX - startX}");
                }
                else
                {
                     // Handle vertical line if needed (not typical for ListBox)
                     double lineX = _isAbove ? relativeStartPoint.X : relativeStartPoint.X + relativeSize.Width;
                     lineX = System.Math.Clamp(lineX, 0, AdornedElement.RenderSize.Width);
                    drawingContext.DrawLine(_pen, new Point(lineX, relativeStartPoint.Y), new Point(lineX, relativeStartPoint.Y + relativeSize.Height));
                }
            }
            catch (InvalidOperationException ex)
            {
                 // Can happen if element is not in visual tree during drag state changes
                 Debug.WriteLine($"Error during InsertionAdorner OnRender (likely element not ready): {ex.Message}");
            }
             catch (Exception ex) // Catch broader exceptions during rendering
             {
                  Debug.WriteLine($"Unexpected error during InsertionAdorner OnRender: {ex.ToString()}");
             }

        }
    }
}
