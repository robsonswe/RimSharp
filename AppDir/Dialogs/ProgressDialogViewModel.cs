using System;
using System.Threading;
using System.Windows.Input;
using RimSharp.Core.Commands; // Keep specific command type if needed
using RimSharp.Core.Commands.Base; // For DelegateCommand
using System.ComponentModel;
using System.Diagnostics; // Added for IDisposable pattern (optional)

namespace RimSharp.AppDir.Dialogs
{
    // Implement IDisposable to manage the CancellationTokenSource
    public class ProgressDialogViewModel : DialogViewModelBase<bool>, IDisposable
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
            IsIndeterminate = isIndeterminate;
            Progress = 0;

            _cts = externalCts ?? new CancellationTokenSource();

            // Use standard DelegateCommand and observe CanCancel property
            // Ensure command creation uses ViewModelBase helpers if possible for consistency,
            // but the explicit DelegateCommand here is also fine.
            CancelCommand = new DelegateCommand(
                () => OnCancel(), // Execute lambda
                () => CanCancel && !_cts.IsCancellationRequested // CanExecute lambda
                ).ObservesProperty(this, nameof(CanCancel)); // Observe property

            // Set initial state AFTER command is created
             CanCancel = canCancel && !_cts.IsCancellationRequested;
             Closeable = closeable; // Set property inherited from DialogViewModelBase
        }

        public void UpdateProgress(int value, string message = null)
        {
             // Use base class disposed flag
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

        public void CompleteOperation(string message = null)
        {
             // Use base class disposed flag
             if (_disposed || _cts.IsCancellationRequested) return;

             RunOnUIThread(() =>
             {
                 // Double check after marshalling
                 if (_disposed || _cts.IsCancellationRequested) return;
                 if (!string.IsNullOrEmpty(message)) Message = message;
                 CanCancel = false; // Disable cancel on completion
                 CloseDialog(true); // Indicate success
             });
        }

        private void OnCancel(string message = null)
        {
             // Use base class disposed flag
             if (_disposed || !CanCancel || _cts.IsCancellationRequested) return;

             RunOnUIThread(() =>
             {
                 // Double check after marshalling
                 if (_disposed || !CanCancel || _cts.IsCancellationRequested) return;
                 if (!string.IsNullOrEmpty(message)) Message = message;
                 IsIndeterminate = true;
                 CanCancel = false; // Disable further cancellation attempts (setter updates command)

                 try
                 {
                     // Check again before cancelling CTS
                     if(!_cts.IsCancellationRequested) _cts.Cancel();
                 }
                 catch (ObjectDisposedException) { /* Ignore */ }

                 Cancelled?.Invoke(this, EventArgs.Empty); // Notify listeners
                 CloseDialog(false); // Indicate cancellation/failure
             });
        }

        public void ForceClose()
        {
             RunOnUIThread(() =>
             {
                 // Use base class disposed flag
                 if (!_disposed)
                 {
                     // Optionally cancel CTS if forcing closed implies cancellation
                     // try { if (!_cts.IsCancellationRequested) _cts.Cancel(); } catch (ObjectDisposedException) { /* ignore */ } catch (Exception ex) { Debug.WriteLine($"Error cancelling CTS on force close: {ex}");}
                     CloseDialog(false); // Indicate failure/external closure
                 }
             });
        }

        // --- IDisposable Implementation ---

        protected override void Dispose(bool disposing)
        {
            // Check the base class flag FIRST
            if (_disposed) // Use the flag from ViewModelBase
            {
                Debug.WriteLine($"[ProgressDialogViewModel] Already disposed or disposing.");
                return;
            }
            Debug.WriteLine($"[ProgressDialogViewModel] Dispose({disposing}) called.");

            if (disposing)
            {
                // --- Derived Class Specific Managed Cleanup ---
                Debug.WriteLine("[ProgressDialogViewModel] Disposing derived managed resources (CTS)...");
                Cancelled = null; // Remove event handlers

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
                 // --- End Derived Class Specific Cleanup ---
            }

            // Clean up unmanaged resources owned *specifically* by ProgressDialogViewModel here (if any)

            // IMPORTANT: Call the base class implementation LAST
            Debug.WriteLine($"[ProgressDialogViewModel] Calling base.Dispose({disposing}). Current base._disposed = {_disposed}");
            base.Dispose(disposing); // This sets the base._disposed flag
            // NOTE: base.Dispose will handle clearing _ownedCommands from ViewModelBase
            Debug.WriteLine($"[ProgressDialogViewModel] Finished Dispose({disposing}). Final base._disposed = {_disposed}");
        }

         // Optional Finalizer: Only keep if ProgressDialogViewModel DIRECTLY owns UNMANAGED resources.
         // If CTS is the only "complex" resource, the Dispose(true) path handles it.
         // Keep it for now as good practice, though CTS is managed.
         ~ProgressDialogViewModel()
         {
             Debug.WriteLine($"[ProgressDialogViewModel] Finalizer called.");
             Dispose(false);
         }
    }
}
