using System;
using System.Threading;
using System.Windows.Input;
using RimSharp.Core.Commands; // Keep specific command type if needed
using RimSharp.Core.Commands.Base;
using System.ComponentModel;
using System.Diagnostics;

namespace RimSharp.AppDir.Dialogs
{
    // Implement IDisposable to manage the CancellationTokenSource
    public class ProgressDialogViewModel : DialogViewModelBase<bool>, IDisposable
    {
        private string _message = string.Empty;
        private int _progress;
        private bool _isIndeterminate;
        private bool _canCancel;

        // Keep the CancellationTokenSource internal
        private readonly CancellationTokenSource _cts;

        public event EventHandler? Cancelled;

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

            private set => SetProperty(ref _canCancel, value);
        }

        public ICommand CancelCommand { get; }

        public CancellationToken CancellationToken => _cts.Token;

        public ProgressDialogViewModel(string title, string message, bool canCancel = false,
            bool isIndeterminate = true, CancellationTokenSource? externalCts = null, bool closeable = true)
            : base(title)
        {
            Message = message;
            IsIndeterminate = isIndeterminate;
            Progress = 0;

            _cts = externalCts ?? new CancellationTokenSource();

CancelCommand = new DelegateCommand(
                () => OnCancel(), // Execute lambda
                () => CanCancel && !_cts.IsCancellationRequested // CanExecute lambda
                ).ObservesProperty(this, nameof(CanCancel)); 
             CanCancel = canCancel && !_cts.IsCancellationRequested;
             Closeable = closeable;
        }

        public void UpdateProgress(int value, string? message = null)
        {

             if (_disposed || _cts.IsCancellationRequested) return;

             RunOnUIThread(() =>
             {
                 // Double check after marshalling
                 if (_disposed || _cts.IsCancellationRequested) return;
                 Progress = value;
                 if (message != null) Message = message;
                 IsIndeterminate = false;
             });
        }

        public void CompleteOperation(string? message = null)
        {

             if (_disposed || _cts.IsCancellationRequested) return;

             RunOnUIThread(() =>
             {
                 // Double check after marshalling
                 if (_disposed || _cts.IsCancellationRequested) return;
                 if (!string.IsNullOrEmpty(message)) Message = message!;
                 CanCancel = false; // Disable cancel on completion
                 CloseDialog(true); // Indicate success
             });
        }

        private void OnCancel(string? message = null)
        {

             if (_disposed || !CanCancel || _cts.IsCancellationRequested) return;

             RunOnUIThread(() =>
             {
                 // Double check after marshalling
                 if (_disposed || !CanCancel || _cts.IsCancellationRequested) return;
                 if (!string.IsNullOrEmpty(message)) Message = message!;
                 IsIndeterminate = true;
                 CanCancel = false; // Disable further cancellation attempts (setter updates command)

                 try
                 {

                     if(!_cts.IsCancellationRequested) _cts.Cancel();
                 }
                 catch (ObjectDisposedException) { /* Ignore */ }

                 Cancelled?.Invoke(this, EventArgs.Empty);
                 CloseDialog(false); // Indicate cancellation/failure
             });
        }

        public void ForceClose()
        {
             RunOnUIThread(() =>
             {

                 if (!_disposed)
                 {
                     // Optionally cancel CTS if forcing closed implies cancellation

                     CloseDialog(false); // Indicate failure/external closure
                 }
             });
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) // Use the flag from ViewModelBase
            {
                Debug.WriteLine($"[ProgressDialogViewModel] Already disposed or disposing.");
                return;
            }
            Debug.WriteLine($"[ProgressDialogViewModel] Dispose({disposing}) called.");

            if (disposing)
            {
                Debug.WriteLine("[ProgressDialogViewModel] Disposing derived managed resources (CTS)...");

try
                {
                    if (!_cts.IsCancellationRequested)
                    {
                         _cts.Cancel();
                         Debug.WriteLine("[ProgressDialogViewModel] CTS Cancelled.");
                    }
                }
                catch (ObjectDisposedException) { Debug.WriteLine("[ProgressDialogViewModel] CTS already disposed on Cancel attempt."); }
                catch (Exception ex) { Debug.WriteLine($"[ProgressDialogViewModel] Error cancelling CTS during dispose: {ex.Message}"); }
                finally
                {
                    _cts?.Dispose();
                    Debug.WriteLine("[ProgressDialogViewModel] CTS Disposed.");
                }
            }

// IMPORTANT: Call the base class implementation LAST
            Debug.WriteLine($"[ProgressDialogViewModel] Calling base.Dispose({disposing}). Current base._disposed = {_disposed}");
            base.Dispose(disposing); // This sets the base._disposed flag

            Debug.WriteLine($"[ProgressDialogViewModel] Finished Dispose({disposing}). Final base._disposed = {_disposed}");
        }

~ProgressDialogViewModel()
         {
             Debug.WriteLine($"[ProgressDialogViewModel] Finalizer called.");
             Dispose(false);
         }
    }
}


