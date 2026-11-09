#nullable enable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RimSharp.Features.Troubleshooting.ViewModels;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.Troubleshooting.Views
{
    public partial class TroubleshootingDialogView : BaseDialog
    {
        public TroubleshootingDialogView()
        {
            InitializeComponent();
        }

        public TroubleshootingDialogView(TroubleshootingViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
