using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace RimSharp.Features.ModManager.Dialogs.DuplicateMods
{
    public partial class DuplicateModDialogView : BaseDialog
    {
        public DuplicateModDialogView()
        {
            InitializeComponent();
        }

        public DuplicateModDialogView(DuplicateModDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.RequestCloseDialog += (sender, e) =>
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => CloseDialog(viewModel));
                }
                else
                {
                    CloseDialog(viewModel);
                }
            };
        }

        private void CloseDialog(DuplicateModDialogViewModel viewModel)
        {
            try
            {
                DialogResult = viewModel.DialogResult;
                Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing dialog: {ex}");
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
                MessageBox.Show($"Error opening URL: {ex.Message}", "Navigation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}