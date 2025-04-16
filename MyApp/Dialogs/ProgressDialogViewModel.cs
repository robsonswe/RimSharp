using System;
using System.Threading;
using System.Windows.Input;
using RimSharp.Core.Commands; // Keep specific command type if needed
using RimSharp.Core.Commands.Base; // For DelegateCommand
using System.ComponentModel;
using System.Diagnostics; // Added for IDisposable pattern (optional)

namespace RimSharp.MyApp.Dialogs
{
    // Implement IDisposable to manage the CancellationTokenSource
    public class ProgressDialogViewModel : DialogViewModelBase<bool>, IDisposable
    {
        private string _message;
        private int _progress;
        private bool _isIndeterminate;
        private bool _canCancel;
        private bool _isDisposed = false; // Track disposal

        // Keep the CancellationTokenSource internal
        private readonly CancellationTokenSource _cts;

        public event EventHandler Cancelled;

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value); // Base handles notification
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value); // Base handles notification
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value); // Base handles notification
        }

        public bool CanCancel
        {
            get => _canCancel;
            // Use base SetProperty, command observation handles CanExecute updates
            private set => SetProperty(ref _canCancel, value);
        }

        public ICommand CancelCommand { get; }

        public CancellationToken CancellationToken => _cts.Token;

        public ProgressDialogViewModel(string title, string message, bool canCancel = false,
            bool isIndeterminate = true, CancellationTokenSource externalCts = null, bool closeable = true)
            : base(title)
        {
            Message = message;
            // CanCancel = canCancel; // Initial value set via property
            IsIndeterminate = isIndeterminate;
            Progress = 0;

            _cts = externalCts ?? new CancellationTokenSource();

            // Use standard DelegateCommand and observe CanCancel property
            CancelCommand = new DelegateCommand(
                () => OnCancel(), // Execute lambda
                () => CanCancel && !_cts.IsCancellationRequested // CanExecute lambda
                ).ObservesProperty(this, nameof(CanCancel)); // Observe property

            // Set initial state AFTER command is created
             CanCancel = canCancel && !_cts.IsCancellationRequested;
             Closeable = closeable;
        }

        public void UpdateProgress(int value, string message = null)
        {
             if (_isDisposed || _cts.IsCancellationRequested) return;

             RunOnUIThread(() => // Ensure UI thread for updates
             {
                Progress = value;
                if (message != null) Message = message;
                IsIndeterminate = false;
             });
        }

        public void CompleteOperation(string message = null)
        {
             if (_isDisposed || _cts.IsCancellationRequested) return;

             RunOnUIThread(() => // Ensure UI thread for updates
             {
                if (!string.IsNullOrEmpty(message)) Message = message;
                CanCancel = false; // Disable cancel on completion
                CloseDialog(true); // Indicate success
             });
        }

        private void OnCancel(string message = null)
        {
             if (_isDisposed || !CanCancel || _cts.IsCancellationRequested) return;

             RunOnUIThread(() => // Ensure UI thread for updates
             {
                 if (!string.IsNullOrEmpty(message)) Message = message;
                 IsIndeterminate = true;
                 CanCancel = false; // Disable further cancellation attempts (setter updates command)

                 try
                 {
                     _cts.Cancel(); // Signal cancellation
                 }
                 catch (ObjectDisposedException) { /* Ignore */ }

                 Cancelled?.Invoke(this, EventArgs.Empty); // Notify listeners
                 CloseDialog(false); // Indicate cancellation/failure
             });
        }

        public void ForceClose()
        {
             // Ensure called on UI thread if manipulating UI state directly
             RunOnUIThread(() =>
             {
                 if (!_isDisposed)
                 {
                    // Optionally cancel CTS if forcing closed implies cancellation
                     // try { if (!_cts.IsCancellationRequested) _cts.Cancel(); } catch { /* ignore */ }
                     CloseDialog(false); // Indicate failure/external closure
                 }
             });
        }

        // --- IDisposable Implementation ---

        protected override void Dispose(bool disposing)
        {
            // Check the base class flag BEFORE doing anything
            // Assuming DialogViewModelBase provides _disposed or inherits it from ViewModelBase
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // --- Derived Class Specific Cleanup ---
                Debug.WriteLine("[ProgressDialogViewModel] Disposing derived resources (CTS)...");
                // Dispose managed resources specific to ProgressDialogViewModel
                try
                {
                    // Cancel first to potentially unblock threads waiting on the token
                    if (!_cts.IsCancellationRequested)
                    {
                         _cts.Cancel();
                    }
                }
                catch (ObjectDisposedException) { /* Ignore */ }
                finally
                {
                    // Ensure CTS is disposed even if Cancel throws (though unlikely for ODE)
                    _cts?.Dispose();
                }
                 Debug.WriteLine("[ProgressDialogViewModel] Disposed CTS.");
                 // --- End Derived Class Specific Cleanup ---
            }
            // Dispose unmanaged resources here if any (specific to ProgressDialogViewModel)

            // IMPORTANT: Call the base class implementation LAST
            // This calls DialogViewModelBase.Dispose(bool), which should in turn call
            // ViewModelBase.Dispose(bool) if it inherits correctly.
             Debug.WriteLine($"[ProgressDialogViewModel] Calling base.Dispose({disposing}).");
            base.Dispose(disposing);
            Debug.WriteLine($"[ProgressDialogViewModel] Finished Dispose({disposing}). _disposed = {_disposed}");
        }

         ~ProgressDialogViewModel()
         {
             Dispose(false);
         }
    }
}
