using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RimSharp.Features.ModManager.Components.ModActions
{
    public partial class ModActionsView : UserControl
    {
        public ModActionsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
