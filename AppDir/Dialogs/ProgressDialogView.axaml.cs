using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.AppDir.Dialogs
{
    public partial class ProgressDialogView : BaseDialog
    {
        public ProgressDialogView()
        {
            InitializeComponent();
        }

        public ProgressDialogView(ProgressDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
