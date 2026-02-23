using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    public partial class ModReplacementDialogView : BaseDialog
    {
        public ModReplacementDialogView()
        {
            InitializeComponent();
        }

        public ModReplacementDialogView(ModReplacementDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
