using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.MissingMods
{
    public partial class MissingModSelectionDialogView : BaseDialog
    {
        public MissingModSelectionDialogView()
        {
            InitializeComponent();
        }

        public MissingModSelectionDialogView(MissingModSelectionDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
