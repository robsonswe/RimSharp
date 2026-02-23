using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Layout;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Features.ModManager.Components.ModList.DragDrop
{
    public class DragAdorner : Control
    {
        private readonly ContentPresenter _contentPresenter;
        private Point _position;

        public DragAdorner(IEnumerable items, IDataTemplate? itemTemplate)
        {
            var itemsList = items.Cast<object>().ToList();
            object displayContent;

            if (itemsList.Count == 1)
            {
                displayContent = itemsList[0];
            }
            else
            {
                displayContent = new TextBlock
                {
                    Text = $"{itemsList.Count} items selected",
                    Padding = new Thickness(10, 5),
                    Background = Brushes.DimGray,
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.Bold
                };
            }

            _contentPresenter = new ContentPresenter
            {
                Content = displayContent,
                ContentTemplate = itemsList.Count == 1 ? itemTemplate : null,
                Opacity = 0.6,
                IsHitTestVisible = false
            };

            // Add the presenter as a logical and visual child
            VisualChildren.Add(_contentPresenter);
            LogicalChildren.Add(_contentPresenter);
            
            IsHitTestVisible = false;
        }

        public void SetPosition(Point pt)
        {
            _position = pt;
            InvalidateArrange();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _contentPresenter.Measure(availableSize);
            return _contentPresenter.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _contentPresenter.Arrange(new Rect(_position.X + 10, _position.Y + 10, 
                _contentPresenter.DesiredSize.Width, _contentPresenter.DesiredSize.Height));
            return finalSize;
        }
    }
}
