using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class IncompatibilityRuleEditorDialogView : BaseDialog
    {
        public IncompatibilityRuleEditorDialogView(IncompatibilityRuleEditorDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestCloseDialog += (s, e) =>
            {
                DialogResult = viewModel.DialogResult;
                Close();
            };
        }
    }
}