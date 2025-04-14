using RimSharp.Infrastructure.Dialog; // For BaseDialog

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    public partial class ModFilterDialogView : BaseDialog
    {
        public ModFilterDialogView(ModFilterDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Handle closing request from ViewModel
             if (viewModel != null)
             {
                 viewModel.RequestCloseDialog += (s, e) => this.Close();
             }
        }
    }
}
