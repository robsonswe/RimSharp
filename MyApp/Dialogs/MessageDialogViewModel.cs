using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using RimSharp.Core.Commands; // Keep specific command type if needed
using RimSharp.Core.Commands.Base; // For DelegateCommand

namespace RimSharp.MyApp.Dialogs
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

        // Button Visibility Flags
        public bool ShowOkButton { get; set; } = true;
        public bool ShowCancelButton { get; set; } = false;
        public bool ShowCopyButton { get; set; }


        // Commands
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyToClipboardCommand { get; }

        public MessageDialogViewModel(string title, string message, MessageDialogType type = MessageDialogType.Information)
            : base(title)
        {
            Message = message;
            DialogType = type;

            // Use base CloseDialog method via standard commands (no dependencies)
            OkCommand = new DelegateCommand(() => CloseDialog(MessageDialogResult.OK));
            CancelCommand = new DelegateCommand(() => CloseDialog(MessageDialogResult.Cancel));
            CopyToClipboardCommand = new DelegateCommand(CopyToClipboard);

            if (type == MessageDialogType.Question)
            {
                ShowOkButton = false;
                // Configure Yes/No visibility if added later
            }
        }
        private void CopyToClipboard()
        {
            try
            {
                 // Consider running on UI thread if called from non-UI context, though unlikely for dialogs
                Clipboard.SetText(Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to copy to clipboard: {ex}");
                // Maybe show error dialog?
            }
        }

    }
}
