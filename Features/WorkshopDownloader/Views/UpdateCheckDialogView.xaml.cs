using System.Windows;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.Features.WorkshopDownloader.Views
{
    // Inherit from BaseDialog instead of Window
    public partial class UpdateCheckDialogView : BaseDialog
    {
        public UpdateCheckDialogView(UpdateCheckDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            // The Title property on BaseDialog (inherited from Window)
            // will be set correctly because the viewModel calls base(title)
            // and the style template binds to the Title property.

            // Handle closing via ViewModel request
            viewModel.RequestCloseDialog += (s, e) =>
            {
                // Ensure DialogResult is set before closing if needed,
                // although the ViewModel's CloseDialog methods should handle this.
                this.DialogResult = (viewModel.DialogResult == UpdateCheckDialogResult.CheckUpdates); // Example: Set standard DialogResult
                this.Close();
            };
        }
    }
}
