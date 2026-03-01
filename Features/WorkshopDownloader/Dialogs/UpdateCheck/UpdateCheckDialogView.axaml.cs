using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.WorkshopDownloader.Dialogs.UpdateCheck
{
    public partial class UpdateCheckDialogView : BaseDialog
    {
        public UpdateCheckDialogView()
        {
            InitializeComponent();
        }

        public UpdateCheckDialogView(UpdateCheckDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
