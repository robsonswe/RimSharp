using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class DependencyRuleEditorDialogView : BaseDialog
    {
        public DependencyRuleEditorDialogView(DependencyRuleEditorDialogViewModel viewModel)
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