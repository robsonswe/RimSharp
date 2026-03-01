using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RimSharp.Features.WorkshopDownloader.Components.StatusBar
{
    public partial class StatusBarView : UserControl
    {
        public StatusBarView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
