using System.Windows.Input;
using RimSharp.Core.Commands;
using RimSharp.MyApp.Dialogs;

namespace RimSharp.MyApp.Dialogs
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

            OkCommand = new RelayCommand(_ => CloseDialog(MessageDialogResult.OK), _ => !string.IsNullOrWhiteSpace(Input));
            CancelCommand = new RelayCommand(_ => CloseDialog(MessageDialogResult.Cancel));
        }
    }
}