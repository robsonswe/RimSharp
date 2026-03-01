using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class DependencyRuleEditorDialogView : BaseDialog
    {
        public DependencyRuleEditorDialogView()
        {
            InitializeComponent();
        }

        public DependencyRuleEditorDialogView(DependencyRuleEditorDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
