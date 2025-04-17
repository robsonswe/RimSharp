using System.Windows.Input;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.AppDir.Dialogs
{
    public class InputDialogViewModel : DialogViewModelBase<MessageDialogResult>
    {
        private string _input;
        public string Input
        {
            get => _input;
            set => SetProperty(ref _input, value);
        }

        public string Message { get; }
        public bool ShowOkButton { get; set; } = true;
        public bool ShowCancelButton { get; set; } = true;

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public InputDialogViewModel(string title, string message, string defaultInput = "")
            : base(title)
        {
            Message = message;
            Input = defaultInput;

            // Use ViewModelBase helper methods
            OkCommand = CreateCommand(
                () => CloseDialog(MessageDialogResult.OK),
                () => !string.IsNullOrWhiteSpace(Input),
                nameof(Input) // Automatically observe Input property
            );

            CancelCommand = CreateCommand(
                () => CloseDialog(MessageDialogResult.Cancel)
            );
        }
    }
}