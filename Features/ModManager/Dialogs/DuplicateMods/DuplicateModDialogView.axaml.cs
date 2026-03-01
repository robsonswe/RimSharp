using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public partial class DuplicateModDialogView : BaseDialog
    {
        public DuplicateModDialogView()
        {
            InitializeComponent();
        }

        public DuplicateModDialogView(DuplicateModDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
