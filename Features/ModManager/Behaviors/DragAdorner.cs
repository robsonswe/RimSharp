using System;
using System.Collections; // Required for IEnumerable
using System.Collections.Generic; // Required for List<>
using System.Linq; // Required for Linq methods
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RimSharp.Features.ModManager.Behaviors
{
    public class DragAdorner : Adorner
    {
        private readonly ContentPresenter _contentPresenter;
        private double _leftOffset;
        private double _topOffset;

        // Updated Constructor
        public DragAdorner(UIElement adornedElement, IEnumerable dragDataItems, DataTemplate itemTemplate)
            : base(adornedElement)
        {
            var itemsList = dragDataItems?.OfType<object>().ToList() ?? new List<object>();
            object displayContent = null;
            DataTemplate displayTemplate = null;

            if (itemsList.Count == 1)
            {
                // If only one item, show it using the original template
                displayContent = itemsList[0];
                displayTemplate = itemTemplate;
            }
            else if (itemsList.Count > 1)
            {
                // If multiple items, show a count
                displayContent = new TextBlock
                {
                    Text = $"{itemsList.Count} items selected",
                    // *** FIX: Provide Left, Top, Right, Bottom values ***
                    Padding = new Thickness(4, 2, 4, 2), // Example: 4 Left/Right, 2 Top/Bottom
                    Background = Brushes.Gray, // Example background
                    Foreground = Brushes.White, // Example text color
                    FontWeight = FontWeights.Bold
                };
                // No specific template needed if setting content directly to a control
            }


            _contentPresenter = new ContentPresenter
            {
                Content = displayContent,
                ContentTemplate = displayTemplate, // Use specific template only if needed
                Opacity = 0.7,
                IsHitTestVisible = false
            };

            // Important: Add the ContentPresenter to the visual tree
            this.AddVisualChild(_contentPresenter);
            this.AddLogicalChild(_contentPresenter);
        }

        // Helper to create a default text template if needed
        private DataTemplate CreateDefaultTextTemplate()
        {
            var template = new DataTemplate();
            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            textBlockFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            // *** FIX: Provide Left, Top, Right, Bottom values ***
            textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2));
            textBlockFactory.SetValue(TextBlock.BackgroundProperty, Brushes.DimGray);
            textBlockFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            template.VisualTree = textBlockFactory;
            return template;
        }


        public void SetPosition(double left, double top)
        {
            // Offset slightly from cursor
            _leftOffset = left + 5;
            _topOffset = top + 5;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            // Adorner position is relative to the AdornedElement's top-left
            // We use GetDesiredTransform to apply the offset
            var adornerLayer = this.Parent as AdornerLayer;
            adornerLayer?.Update(AdornedElement);
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

        protected override Visual GetVisualChild(int index)
        {
             if (index == 0) return _contentPresenter;
             throw new ArgumentOutOfRangeException(nameof(index));
        }


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
