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
        // No _viewModel field needed
        // No _isClosing field needed

        public DependencyResolutionDialogView() // Keep default constructor
        {
            InitializeComponent();
            // Optional: Add specific initialization for this view if needed
        }

        public DependencyResolutionDialogView(DependencyResolutionDialogViewModel viewModel) : this()
        {
            // The BaseDialog constructor handles DataContextChanged event subscription
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Debug.WriteLine($"[DependencyResolutionDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // No ViewModel_RequestCloseDialog handler needed
        // No CloseDialogInternal method needed

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
            Debug.WriteLine($"[DependencyResolutionDialogView] OnClosed for {this.Title}.");
            base.OnClosed(e);
        }
    }
}
