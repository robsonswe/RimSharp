using System;
using System.Windows.Input;
using RimSharp.Core.Commands;

namespace RimSharp.MyApp.Dialogs
{
    public class ProgressDialogViewModel : DialogViewModelBase<bool>
    {
        private string _title;
        private string _message;
        private int _progress;
        private bool _isIndeterminate;
        private bool _canCancel;

        public event EventHandler Cancelled;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }

        public bool CanCancel
        {
            get => _canCancel;
            set => SetProperty(ref _canCancel, value);
        }

        public ICommand CancelCommand { get; }

        public void Complete(string message = null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Message = message;
            }
            CloseDialog(true);
        }

        public void Cancel(string message = null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Message = message;
            }
            Cancelled?.Invoke(this, EventArgs.Empty);
            CloseDialog(false);
        }

        public void ForceClose()
        {
            CloseDialog(false);
        }


        public ProgressDialogViewModel(string title, string message, bool canCancel = false, bool isIndeterminate = true)
            : base(title)
        {
            Title = title;
            Message = message;
            CanCancel = canCancel;
            IsIndeterminate = isIndeterminate;
            Progress = 0;

            CancelCommand = new RelayCommand(_ => Cancel());
        }

        public void Cancel()
        {
            CloseDialog(false);
        }

        public void Complete()
        {
            CloseDialog(true);
        }
    }
}
