using RimSharp.Infrastructure.Dialog;
using Avalonia.Input;

namespace RimSharp.Features.ModManager.Dialogs.ModSelector
{
    public partial class ModSelectorDialogView : BaseDialog
    {
        public ModSelectorDialogView()
        {
            InitializeComponent();
        }

        public ModSelectorDialogView(ModSelectorDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is ModSelectorDialogViewModel viewModel && viewModel.ConfirmCommand.CanExecute(null))
            {
                viewModel.ConfirmCommand.Execute(null);
            }
        }
    }
}
