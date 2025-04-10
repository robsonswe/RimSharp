using System;
using System.Threading;
using System.Windows.Input;
using RimSharp.Core.Commands;

namespace RimSharp.MyApp.Dialogs
{
    public class ProgressDialogViewModel : DialogViewModelBase<bool>
    {
        private string _message;
        private int _progress;
        private bool _isIndeterminate;
        private bool _canCancel;

        private CancellationTokenSource _cts;


        public event EventHandler Cancelled;

        public string Message
        {
            get => _message;
            // Make sure ViewModelBase and SetProperty are accessible
            // If ViewModelBase is in RimSharp.MyApp.AppFiles, add: using RimSharp.MyApp.AppFiles;
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

        // Constructor: Calls base(title) to set the inherited Title property
            public ProgressDialogViewModel(string title, string message, bool canCancel = false, 
                bool isIndeterminate = true, CancellationTokenSource cts = null)
                : base(title)
            {
                Message = message;
                CanCancel = canCancel;
                IsIndeterminate = isIndeterminate;
                Progress = 0;
                _cts = cts;

                CancelCommand = new RelayCommand(_ => OnCancel());
            }


        public void UpdateProgress(int value, string message = null)
        {
             Progress = value;
             if(message != null)
             {
                 Message = message;
             }
             IsIndeterminate = false; // Typically switch off indeterminate when progress is reported
        }


        public void CompleteOperation(string message = null) // Renamed from Complete to avoid conflict
        {
            if (!string.IsNullOrEmpty(message))
            {
                Message = message;
            }
            CloseDialog(true); // Use the base class method to close with result
        }

        // Renamed from Cancel to avoid conflict with property/event names if any ambiguity
        // Also matches the CancelCommand action
            public void OnCancel(string message = null)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Message = message;
                }
                
                // Cancel the operation
                _cts?.Cancel();
                
                // Raise the Cancelled event
                Cancelled?.Invoke(this, EventArgs.Empty);
                
                // Close the dialog
                CloseDialog(false);
            }


        // Kept for cases where external code needs to force close without triggering Cancelled event logic
        public void ForceClose()
        {
             // Directly close using the base class mechanism with a 'false' result
             CloseDialog(false);
        }
    }
}