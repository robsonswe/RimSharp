using RimSharp.Handlers; // Ensure RelayCommand is accessible
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace RimSharp.ViewModels.Dialogs
{
    public enum MessageDialogType
    {
        Information,
        Warning,
        Error,
        Question // For Yes/No scenarios later
    }

    public enum MessageDialogResult
    {
        OK,
        Cancel,
        Yes,
        No
    }

    // Use the generic base to return a result (e.g., OK/Cancel)
    public class MessageDialogViewModel : DialogViewModelBase<MessageDialogResult>
    {
        public string Message { get; }
        public MessageDialogType DialogType { get; }

        // Button Visibility Flags (can add Yes/No later)
        public bool ShowOkButton { get; set; } = true;
        public bool ShowCancelButton { get; set; } = false;
        // public bool ShowYesButton { get; set; } = false;
        // public bool ShowNoButton { get; set; } = false;

        public bool ShowCopyButton { get; set; }


        // Specific commands binding to CloseDialog with appropriate result
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }
        // public ICommand YesCommand { get; }
        // public ICommand NoCommand { get; }

        public ICommand CopyToClipboardCommand { get; }

        public MessageDialogViewModel(string title, string message, MessageDialogType type = MessageDialogType.Information)
            : base(title)
        {
            Message = message;
            DialogType = type;

            // Initialize commands to close with specific results
            OkCommand = new RelayCommand(_ => CloseDialog(MessageDialogResult.OK));
            CancelCommand = new RelayCommand(_ => CloseDialog(MessageDialogResult.Cancel));
            // YesCommand = new RelayCommand(_ => CloseDialog(MessageDialogResult.Yes));
            // NoCommand = new RelayCommand(_ => CloseDialog(MessageDialogResult.No));

            CopyToClipboardCommand = new RelayCommand(_ => CopyToClipboard());

            // Configure default buttons based on type (optional refinement)
            if (type == MessageDialogType.Question)
            {
                ShowOkButton = false;
                // ShowYesButton = true;
                // ShowNoButton = true;
            }
        }
        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(Message);
                // Optionally show a brief confirmation
                // You could use a separate dialog service call here if needed
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to copy to clipboard: {ex}");
            }
        }

    }
}
