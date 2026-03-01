using System.Collections;
using Avalonia;

namespace RimSharp.Features.ModManager.Components.ModList
{
    public class SelectedItemsHolder : AvaloniaObject
    {
        public static readonly StyledProperty<IList?> SelectedItemsProperty =
            AvaloniaProperty.Register<SelectedItemsHolder, IList?>(nameof(SelectedItems));

        public IList? SelectedItems
        {
            get => GetValue(SelectedItemsProperty);
            set => SetValue(SelectedItemsProperty, value);
        }
    }
}
