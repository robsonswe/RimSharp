using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RimSharp.Views.Modules.Mods.DragAdorner
{
    public class InsertionAdorner : Adorner
    {
        private readonly bool _isSeparatorHorizontal;
        private readonly Pen _pen;
        private readonly double _left;
        private readonly double _top;

        public InsertionAdorner(UIElement adornedElement, UIElement adornedContainer, 
                              bool isSeparatorHorizontal, Brush brush, double thickness) 
            : base(adornedElement)
        {
            _isSeparatorHorizontal = isSeparatorHorizontal;
            _pen = new Pen(brush, thickness);
            
            var containerPos = adornedContainer.TransformToAncestor(adornedElement).Transform(new Point(0, 0));
            _left = containerPos.X;
            _top = containerPos.Y + (isSeparatorHorizontal ? 0 : adornedContainer.RenderSize.Height);
            
            // Remove this line - don't add the adorner here
            // _adornerLayer.Add(this);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(
                _left,
                _top,
                _isSeparatorHorizontal ? AdornedElement.RenderSize.Width : 0,
                _isSeparatorHorizontal ? 0 : AdornedElement.RenderSize.Height);

            drawingContext.DrawRectangle(_pen.Brush, _pen, rect);
        }
    }
}