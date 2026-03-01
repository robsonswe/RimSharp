using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.Strip
{
    public partial class StripModsDialogView : BaseDialog
    {
        public StripModsDialogView()
        {
            InitializeComponent();
        }

        public StripModsDialogView(StripDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
