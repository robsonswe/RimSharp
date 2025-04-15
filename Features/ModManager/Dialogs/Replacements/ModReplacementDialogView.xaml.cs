using RimSharp.Infrastructure.Dialog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace RimSharp.Features.ModManager.Dialogs.Replacements
{
    /// <summary>
    /// Interaction logic for ModReplacementDialogView.xaml
    /// </summary>
    public partial class ModReplacementDialogView : BaseDialog
    {
        private readonly ModReplacementDialogViewModel _viewModel;

        public ModReplacementDialogView()
        {
            InitializeComponent();
        }

        public ModReplacementDialogView(ModReplacementDialogViewModel viewModel) : this()
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            
            // Subscribe to the close event
            _viewModel.RequestCloseDialog += ViewModel_RequestCloseDialog;
            
            // Make sure to unsubscribe when the dialog is closed
            Closed += (s, e) => 
            {
                _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            };
        }

        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => CloseDialog());
            }
            else
            {
                CloseDialog();
            }
        }

        private void CloseDialog()
        {
            try
            {
                // Set DialogResult to true if the result is not Cancel
                // This fixes the type mismatch
                DialogResult = _viewModel.DialogResult != ModReplacementDialogResult.Cancel;
                Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing dialog: {ex}");
                // Attempt to close anyway if there was an error setting the dialog result
                Close();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                };
                Process.Start(psi);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening URL: {ex}");
                MessageBox.Show($"Failed to open URL: {ex.Message}", "Navigation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}