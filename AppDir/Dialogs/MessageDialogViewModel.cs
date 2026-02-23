using System;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace RimSharp.AppDir.Dialogs
{
    public enum MessageDialogResult
    {
        None, OK, Yes, No, Cancel
    }

    public enum MessageDialogType
    {
        Information, Warning, Error, Question
    }

    public class MessageDialogViewModel : DialogViewModelBase<MessageDialogResult>
    {
        private string _message;
        private MessageDialogType _dialogType;
        private bool _showOkButton = true;
        private bool _showCancelButton = false;
        private bool _showCopyButton = false;

        public string Message { get => _message; set => SetProperty(ref _message, value); }
        public MessageDialogType DialogType { get => _dialogType; set => SetProperty(ref _dialogType, value); }
        public bool ShowOkButton { get => _showOkButton; set => SetProperty(ref _showOkButton, value); }
        public bool ShowCancelButton { get => _showCancelButton; set => SetProperty(ref _showCancelButton, value); }
        public bool ShowCopyButton { get => _showCopyButton; set => SetProperty(ref _showCopyButton, value); }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyToClipboardCommand { get; }

        public MessageDialogViewModel(string title, string message, MessageDialogType dialogType = MessageDialogType.Information)
            : base(title)
        {
            _message = message;
            _dialogType = dialogType;

            OkCommand = CreateCommand(() => CloseDialog(MessageDialogResult.OK));
            CancelCommand = CreateCommand(() => CloseDialog(MessageDialogResult.Cancel));
            CopyToClipboardCommand = CreateCommand(CopyToClipboard);
        }

        private async void CopyToClipboard()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = desktop.MainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(Message);
                }
            }
        }
    }
}
