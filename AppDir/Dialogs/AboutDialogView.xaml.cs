using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using RimSharp.Infrastructure.Dialog;

namespace RimSharp.AppDir.Dialogs
{
    public partial class AboutDialogView : BaseDialog
    {
        public AboutDialogView()
        {
            InitializeComponent();
        }

        public AboutDialogView(AboutDialogViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch
            {
                // Ignore errors opening browser
            }
        }
    }
}
