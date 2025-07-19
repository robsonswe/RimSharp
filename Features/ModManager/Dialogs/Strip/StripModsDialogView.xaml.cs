using RimSharp.Infrastructure.Dialog;
using System.Windows;

namespace RimSharp.Features.ModManager.Dialogs.Strip
{
    /// <summary>
    /// Interaction logic for StripModsDialogView.xaml.
    /// A dialog that allows users to select and remove unnecessary files and folders from mods.
    /// </summary>
    public partial class StripModsDialogView : BaseDialog
    {
        public StripModsDialogView(StripDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.RequestCloseDialog += (s, e) =>
            {
                this.Close();
            };
        }
    }
}