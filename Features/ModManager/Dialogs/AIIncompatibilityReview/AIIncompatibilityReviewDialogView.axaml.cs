using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.AIIncompatibilityReview
{
    public partial class AIIncompatibilityReviewDialogView : BaseDialog
    {
        public AIIncompatibilityReviewDialogView()
        {
            InitializeComponent();
        }

        public AIIncompatibilityReviewDialogView(AIIncompatibilityReviewDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
