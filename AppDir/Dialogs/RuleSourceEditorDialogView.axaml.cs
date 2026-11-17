using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.AppDir.Dialogs
{
    public partial class RuleSourceEditorDialogView : BaseDialog
    {
        public RuleSourceEditorDialogView()
        {
            InitializeComponent();
        }

        public RuleSourceEditorDialogView(RuleSourceEditorDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
