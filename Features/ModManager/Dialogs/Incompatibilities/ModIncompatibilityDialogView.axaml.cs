using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;
using RimSharp.AppDir.Dialogs;

namespace RimSharp.Features.ModManager.Dialogs.Incompatibilities
{
    public partial class ModIncompatibilityDialogView : BaseDialog
    {
        public ModIncompatibilityDialogView()
        {
            InitializeComponent();
        }

        public ModIncompatibilityDialogView(ModIncompatibilityDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
