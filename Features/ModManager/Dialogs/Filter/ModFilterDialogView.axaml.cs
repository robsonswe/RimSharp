using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    public partial class ModFilterDialogView : BaseDialog
    {
        public ModFilterDialogView()
        {
            InitializeComponent();
        }

        public ModFilterDialogView(ModFilterDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
