using System.Collections;
using System.Windows;

namespace RimSharp.Features.ModManager.Components.ModList
{
    public class SelectedItemsHolder : Freezable
    {
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.Register("SelectedItems", typeof(IList), typeof(SelectedItemsHolder), new PropertyMetadata(null));

        public IList SelectedItems
        {
            get { return (IList)GetValue(SelectedItemsProperty); }
            set { SetValue(SelectedItemsProperty, value); }
        }

        protected override Freezable CreateInstanceCore()
        {
            return new SelectedItemsHolder();
        }
    }
}