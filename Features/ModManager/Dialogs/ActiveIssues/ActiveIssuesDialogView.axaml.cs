using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.ModManager.Dialogs.ActiveIssues
{
    public partial class ActiveIssuesDialogView : BaseDialog
    {
        public ActiveIssuesDialogView()
        {
            InitializeComponent();
        }

        public ActiveIssuesDialogView(ActiveIssuesDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }
    }
}
