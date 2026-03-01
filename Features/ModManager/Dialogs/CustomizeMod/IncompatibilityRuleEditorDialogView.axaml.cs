using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class IncompatibilityRuleEditorDialogView : BaseDialog
    {
        public IncompatibilityRuleEditorDialogView()
        {
            InitializeComponent();
        }

        public IncompatibilityRuleEditorDialogView(IncompatibilityRuleEditorDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
