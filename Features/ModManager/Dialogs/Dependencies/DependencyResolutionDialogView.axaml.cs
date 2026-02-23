using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.Dependencies
{
    public partial class DependencyResolutionDialogView : BaseDialog
    {
        public DependencyResolutionDialogView()
        {
            InitializeComponent();
        }

        public DependencyResolutionDialogView(DependencyResolutionDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
