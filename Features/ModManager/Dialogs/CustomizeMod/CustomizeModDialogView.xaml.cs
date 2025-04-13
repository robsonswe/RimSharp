using System.Windows;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class CustomizeModDialogView : BaseDialog
    {
        public CustomizeModDialogView(CustomizeModDialogViewModel viewModel)
        {
            InitializeComponent();
            
            DataContext = viewModel;
            
            // Hook up to the view model's request close event
            viewModel.RequestCloseDialog += (s, e) => Close();
        }
    }
}