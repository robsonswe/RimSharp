using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.RulesViewer
{
    public partial class RulesViewerDialogView : BaseDialog
    {
        public RulesViewerDialogView()
        {
            InitializeComponent();
        }

        public RulesViewerDialogView(RulesViewerDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
