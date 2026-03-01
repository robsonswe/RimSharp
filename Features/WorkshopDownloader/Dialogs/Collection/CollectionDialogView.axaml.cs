using Avalonia;
using Avalonia.Markup.Xaml;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.WorkshopDownloader.Dialogs.Collection
{
    public partial class CollectionDialogView : BaseDialog
    {
        public CollectionDialogView()
        {
            InitializeComponent();
        }

        public CollectionDialogView(CollectionDialogViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
