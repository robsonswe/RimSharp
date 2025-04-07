using System.Windows;
using RimSharp.MyApp.Dialogs;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Dialog
{
    public class DialogService : IDialogService
    {
        // Helper to create and show the dialog
        private MessageDialogResult ShowDialogInternal(MessageDialogViewModel viewModel)
        {
            var dialog = new MessageDialogView(viewModel)
            {
                Owner = Application.Current.MainWindow, // Ensure it's modal to the main window
                WindowStartupLocation = WindowStartupLocation.CenterOwner // Redundant if set in style, but safe
            };

            dialog.ShowDialog(); // Shows modally and blocks execution

            // Return the result from the ViewModel
            return viewModel.DialogResult;
        }

        public void ShowInformation(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Information);
            ShowDialogInternal(viewModel);
            // Result ignored for simple info
        }

        public void ShowWarning(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Warning);
            ShowDialogInternal(viewModel);
             // Result ignored
        }

        public void ShowError(string title, string message)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Error);
            ShowDialogInternal(viewModel);
             // Result ignored
        }

        public MessageDialogResult ShowConfirmation(string title, string message, bool showCancel = false)
        {
            var viewModel = new MessageDialogViewModel(title, message, MessageDialogType.Question)
            {
                // Configure buttons for confirmation
                ShowOkButton = false, // Assuming Yes/No instead of OK
                // ShowYesButton = true,
                // ShowNoButton = true,
                ShowCancelButton = showCancel
            };
            // TODO: Add Yes/No buttons to MessageDialogView.xaml and MessageDialogViewModel
            // For now, let's fake it with OK/Cancel if needed, or just OK
            // Re-enable OK for a simple confirmation for now:
            viewModel.ShowOkButton = true;
            viewModel.ShowCancelButton = true; // Example: Make it an OK/Cancel confirmation

            return ShowDialogInternal(viewModel);
        }

        public void ShowMessageWithCopy(string title, string message, MessageDialogType dialogType = MessageDialogType.Information)
        {
            var viewModel = new MessageDialogViewModel(title, message, dialogType)
            {
                ShowCopyButton = true // We'll add this property to the ViewModel
            };
            ShowDialogInternal(viewModel);
        }

    }
}
