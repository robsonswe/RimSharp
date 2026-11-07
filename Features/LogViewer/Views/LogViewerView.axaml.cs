using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Features.LogViewer.ViewModels;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.LogViewer.Views
{
    public partial class LogViewerView : BaseDialog
    {
        public LogViewerView()
        {
            InitializeComponent();
        }

        public LogViewerView(LogViewerViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
