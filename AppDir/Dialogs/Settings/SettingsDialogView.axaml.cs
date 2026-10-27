using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.AppDir.Dialogs
{
    public partial class SettingsDialogView : BaseDialog
    {
        public SettingsDialogView()
        {
            InitializeComponent();
        }

        public SettingsDialogView(SettingsDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
