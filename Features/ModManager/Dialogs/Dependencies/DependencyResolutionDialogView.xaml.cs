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
        // Keep a reference to the ViewModel to access its properties in the event handler
        private DependencyResolutionDialogViewModel _viewModel;

        public DependencyResolutionDialogView() // Keep default constructor
        {
            InitializeComponent();
            // Optional: Add specific initialization for this view if needed
        }

        public DependencyResolutionDialogView(DependencyResolutionDialogViewModel viewModel) : this()
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // *** ADD THIS EVENT SUBSCRIPTION ***
            _viewModel.RequestCloseDialog += ViewModel_RequestCloseDialog;
            // *** END ADDITION ***

            Debug.WriteLine($"[DependencyResolutionDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // *** ADD THIS EVENT HANDLER ***
        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            Debug.WriteLine($"[DependencyResolutionDialogView] ViewModel requested close for '{this.Title}'. VM DialogResultForWindow = {_viewModel?.DialogResultForWindow}");
            if (_viewModel != null)
            {
                // Set the Window's DialogResult based on the ViewModel's mapped value
                this.DialogResult = _viewModel.DialogResultForWindow;
            }
            // Now close the dialog window
            this.Close();
        }
        // *** END ADDITION ***


        // Keep Hyperlink navigation if needed by your XAML
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                if (e.Uri != null && e.Uri.IsAbsoluteUri)
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening URL '{e.Uri?.AbsoluteUri}': {ex}");
                MessageBox.Show($"Failed to open the link: {ex.Message}", "Navigation Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // BaseDialog handles cleanup in its OnClosed override
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[DependencyResolutionDialogView] OnClosed for {this.Title}. Cleaning up event handler.");
            // *** ADD CLEANUP ***
            // Unsubscribe to prevent potential memory leaks if the view were reused (unlikely for ShowDialog)
            if (_viewModel != null)
            {
                _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
            }
            // *** END ADDITION ***
            base.OnClosed(e);
        }
    }
}