using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class CustomizeModDialogView : BaseDialog
    {
        public CustomizeModDialogView()
        {
            InitializeComponent();
        }

        public CustomizeModDialogView(CustomizeModDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
