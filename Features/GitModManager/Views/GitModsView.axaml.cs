using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RimSharp.Features.GitModManager.ViewModels;
using System.Diagnostics;

namespace RimSharp.Features.GitModManager.Views
{
    public partial class GitModsView : UserControl
    {
        public GitModsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
