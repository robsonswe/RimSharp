using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Layout;

namespace RimSharp.Features.ModManager.Components.ModList.DragDrop
{
    public class InsertionAdorner : Control
    {
        private readonly bool _isAbove;
        private readonly Control _relativeTo;
        private readonly IBrush _brush;
        private readonly double _thickness;

        public InsertionAdorner(Control relativeTo, bool isAbove, IBrush brush, double thickness)
        {
            _relativeTo = relativeTo;
            _isAbove = isAbove;
            _brush = brush;
            _thickness = thickness;
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context)
        {
            if (_relativeTo == null) return;

            // Get the relative container's bounds
            var bounds = _relativeTo.Bounds;
            
            // The position is already relative to the parent (ListBox)
            double y = _isAbove ? bounds.Top : bounds.Bottom;
            
            // Draw a horizontal line across the parent's width
            // We use the parent's bounds for the X coordinates
            if (Parent is Visual parentVisual)
            {
                context.DrawLine(new Pen(_brush, _thickness), 
                    new Point(5, y), 
                    new Point(parentVisual.Bounds.Width - 5, y));
            }
        }
    }
}
