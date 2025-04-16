using RimSharp.Infrastructure.Dialog; // For BaseDialog
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace RimSharp.Features.ModManager.Dialogs.Dependencies
{
    /// <summary>
    /// Interaction logic for DependencyResolutionDialogView.xaml
    /// </summary>
    public partial class DependencyResolutionDialogView : BaseDialog
    {
        private readonly DependencyResolutionDialogViewModel _viewModel;

        public DependencyResolutionDialogView()
        {
            InitializeComponent();
        }

        public DependencyResolutionDialogView(DependencyResolutionDialogViewModel viewModel) : this()
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // Subscribe to the close request event from the ViewModel
            _viewModel.RequestCloseDialog += ViewModel_RequestCloseDialog;

            // Unsubscribe when the dialog is closed to prevent memory leaks
            Closed += (s, e) =>
            {
                if (_viewModel != null)
                {
                    _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
                }
            };
        }

        // Handles the close request from the ViewModel
        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Ensure we're on the UI thread before closing
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => CloseDialogInternal());
            }
            else
            {
                CloseDialogInternal();
            }
        }

        // Performs the actual closing logic
        private void CloseDialogInternal()
        {
            try
            {
                // Set DialogResult based on the ViewModel's result (true if not Cancel)
                // This tells the ShowDialog() caller whether the action was confirmed.
                DialogResult = _viewModel.DialogResult != DependencyResolutionDialogResult.Cancel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting DialogResult: {ex}");
                // Optionally log this error
            }
            finally
            {
                // Ensure the window closes even if setting DialogResult fails
                Close();
            }
        }

        // Handles hyperlink navigation (copied from other dialogs)
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                if (e.Uri != null && e.Uri.IsAbsoluteUri)
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true // Use the default browser
                    };
                    Process.Start(psi);
                    e.Handled = true; // Mark the event as handled
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening URL '{e.Uri?.AbsoluteUri}': {ex}");
                // Optionally show an error message to the user
                MessageBox.Show($"Failed to open the link: {ex.Message}", "Navigation Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
