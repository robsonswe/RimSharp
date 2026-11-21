using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class RelationRuleEditorDialogView : BaseDialog
    {
        public RelationRuleEditorDialogView()
        {
            InitializeComponent();
        }

        public RelationRuleEditorDialogView(RelationRuleEditorDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
