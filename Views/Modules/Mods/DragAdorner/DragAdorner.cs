using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RimSharp.Views.Modules.Mods.DragAdorner
{
    public class DragAdorner : Adorner
    {
        private readonly ContentPresenter _contentPresenter;
        private double _leftOffset;
        private double _topOffset;

        public DragAdorner(UIElement adornedElement, object dragData, DataTemplate dragTemplate) 
            : base(adornedElement)
        {
            _contentPresenter = new ContentPresenter
            {
                Content = dragData,
                ContentTemplate = dragTemplate,
                Opacity = 0.7,
                IsHitTestVisible = false
            };
        }

        public void SetPosition(double left, double top)
        {
            _leftOffset = left;
            _topOffset = top;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            var adornerLayer = (AdornerLayer)Parent;
            if (adornerLayer != null)
            {
                adornerLayer.Update(AdornedElement);
            }
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _contentPresenter.Measure(constraint);
            return _contentPresenter.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _contentPresenter.Arrange(new Rect(finalSize));
            return finalSize;
        }

        protected override Visual GetVisualChild(int index) => _contentPresenter;

        protected override int VisualChildrenCount => 1;

        public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
        {
            var result = new GeneralTransformGroup();
            result.Children.Add(base.GetDesiredTransform(transform));
            result.Children.Add(new TranslateTransform(_leftOffset, _topOffset));
            return result;
        }
    }
}