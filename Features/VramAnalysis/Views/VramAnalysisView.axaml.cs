using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RimSharp.Features.VramAnalysis.ViewModels;
using System.Diagnostics;

namespace RimSharp.Features.VramAnalysis.Views
{
    public partial class VramAnalysisView : UserControl
    {
        public VramAnalysisView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
