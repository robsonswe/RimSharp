using System.Windows;
using RimSharp.Infrastructure.Dialog;

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
            DataContext = viewModel;
            viewModel.RequestClose += (result) => 
            {
                DialogResult = result;
                Close();
            };
        }
    }
}