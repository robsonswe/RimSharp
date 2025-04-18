using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public partial class DuplicateModDialogView : BaseDialog
    {
        // No _viewModel field needed
        // No _isClosing field needed

        public DuplicateModDialogView() // Keep default constructor
        {
            InitializeComponent();
        }

        public DuplicateModDialogView(DuplicateModDialogViewModel viewModel) : this()
        {
            // The BaseDialog constructor handles DataContextChanged event subscription
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Debug.WriteLine($"[DuplicateModDialogView] Constructor with VM finished for {viewModel?.Title}. DataContext set.");
        }

        // No inline handler for RequestCloseDialog needed
        // No CloseDialog method needed

        // Keep Hyperlink navigation if needed by your XAML
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
                MessageBox.Show($"Error opening URL: {ex.Message}", "Navigation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // BaseDialog handles cleanup in its OnClosed override
        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine($"[DuplicateModDialogView] OnClosed for {this.Title}.");
            base.OnClosed(e);
        }
    }
}
