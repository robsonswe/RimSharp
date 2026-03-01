using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RimSharp.Features.ModManager.Views
{
    public partial class ModsView : UserControl
    {
        public ModsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
