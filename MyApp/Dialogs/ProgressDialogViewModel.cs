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

        // Keep the CancellationTokenSource internal
        private readonly CancellationTokenSource _cts;

        public event EventHandler Cancelled;

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
            private set => SetProperty(ref _canCancel, value); // Make setter private if only set internally
        }

        public ICommand CancelCommand { get; }

        // Public property to get the token controlled by this dialog's cancellation
        public CancellationToken CancellationToken => _cts.Token;

        public ProgressDialogViewModel(string title, string message, bool canCancel = false,
            bool isIndeterminate = true, CancellationTokenSource externalCts = null)
            : base(title)
        {
            Message = message;
            CanCancel = canCancel;
            IsIndeterminate = isIndeterminate;
            Progress = 0;

            // Use the provided CTS if available, otherwise create an internal one.
            // This allows external callers (like the DialogService) to potentially provide
            // a source they want to control, but ensures we always have one internally.
            _cts = externalCts ?? new CancellationTokenSource();

            // Only enable the command if cancellation is allowed
            CancelCommand = new RelayCommand(_ => OnCancel(), _ => CanCancel);
        }

        // REMOVED: LinkCancellationTokenSource is no longer needed

        public void UpdateProgress(int value, string message = null)
        {
            // Prevent updates if cancelled? Optional, depends on desired behavior.
            // if (_cts.IsCancellationRequested) return;

            Progress = value;
            if (message != null)
            {
                Message = message;
            }
            IsIndeterminate = false; // Switch off indeterminate when progress is reported
        }

        public void CompleteOperation(string message = null)
        {
            // Prevent updates if cancelled?
            // if (_cts.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(message))
            {
                Message = message;
            }
            // Disable cancel button on completion?
            // CanCancel = false;
            // (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged(); // Requires RelayCommand type access
            CloseDialog(true); // Indicate success
        }

        private void OnCancel(string message = null)
        {
            if (!CanCancel || _cts.IsCancellationRequested) return; // Don't process if already cancelled or not allowed

            if (!string.IsNullOrEmpty(message))
            {
                Message = message;
            }
            IsIndeterminate = true; // Often good to show indeterminate during cancellation cleanup

            // Disable further cancellation attempts
            // CanCancel = false;
            // (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();

            try
            {
                _cts.Cancel(); // Signal cancellation
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already disposed, though should ideally not happen
            }

            Cancelled?.Invoke(this, EventArgs.Empty); // Notify listeners

            CloseDialog(false); // Indicate cancellation/failure
        }

        public void ForceClose()
        {
            // Ensure CTS is cancelled if forcing closed, maybe? Or assume external cause.
            CloseDialog(false); // Indicate failure/external closure
        }

        // Optional: Dispose the CTS when the dialog view model is no longer needed
        // This requires DialogViewModelBase or the owner to handle disposal.
        // Example:
        // protected override void Dispose(bool disposing)
        // {
        //     if (disposing)
        //     {
        //         _cts?.Dispose();
        //     }
        //     base.Dispose(disposing);
        // }

    }
}