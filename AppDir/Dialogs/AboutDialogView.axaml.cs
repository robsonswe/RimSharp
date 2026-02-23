using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.AppDir.Dialogs
{
    public partial class AboutDialogView : BaseDialog
    {
        public AboutDialogView()
        {
            InitializeComponent();
        }

        public AboutDialogView(AboutDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
