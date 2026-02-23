using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RimSharp.AppDir.MainPage
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
