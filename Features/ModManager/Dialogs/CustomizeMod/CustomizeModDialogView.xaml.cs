using System.Windows;
using RimSharp.Infrastructure.Dialog;
using System; // Add for EventArgs
using System.Diagnostics; // Add for Debug

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class CustomizeModDialogView : BaseDialog
    {
        // Keep track of the ViewModel to unsubscribe later
        private CustomizeModDialogViewModel _viewModel;

        public CustomizeModDialogView(CustomizeModDialogViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            // Hook up to the view model's request close event
            _viewModel.RequestCloseDialog += ViewModel_RequestCloseDialog;
        }

        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Optionally set DialogResult based on VM if CustomizeModDialogViewModel returned a result
             if (_viewModel.DialogResult == ModCustomizationResult.Save)
             {
                  this.DialogResult = true; // Example mapping
                  Debug.WriteLine($"[CustomizeModDialogView] Setting DialogResult to true");
             }
             else
             {
                  this.DialogResult = false; // Example mapping
                  Debug.WriteLine($"[CustomizeModDialogView] Setting DialogResult to false");
             }

            Debug.WriteLine($"[CustomizeModDialogView] Calling Close() for {this.Title}.");
            Close(); // Triggers BaseDialog.OnClosing
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe to prevent memory leaks
            if (_viewModel != null)
            {
                _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
                _viewModel = null; // Clear reference
            }
            base.OnClosed(e);
            Debug.WriteLine($"[CustomizeModDialogView] Closed and cleaned up: {this.Title}");
        }
    }
}
