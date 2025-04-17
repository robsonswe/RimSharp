using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading; // Added for CancellationToken
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;
using RimSharp.Core.Commands.Composite;
using RimSharp.Core.Extensions;
using RimSharp.Core.Services.Commanding; // Assuming IModCommandService is here or accessible

namespace RimSharp.AppDir.AppFiles
{
    /// <summary>
    /// Base class for all ViewModels in the application, providing property change notification
    /// and abstracted command creation/management.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable // Make abstract if never instantiated directly
    {
        // Optional: If ViewModels need access to a global command service
        private readonly IModCommandService _commandService;

        // Collection to hold commands created by this ViewModel instance for automatic disposal
        private readonly List<IDisposable> _ownedCommands = new List<IDisposable>();
        protected bool _disposed = false;

        /// <summary>
        /// Event that is raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewModelBase"/> class.
        /// </summary>
        protected ViewModelBase() // Changed to protected
        {
            // Default constructor
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewModelBase"/> class with command service support.
        /// </summary>
        /// <param name="commandService">The command service for accessing global commands.</param>
        protected ViewModelBase(IModCommandService commandService) // Changed to protected
        {
            _commandService = commandService;
        }

        #region Property Notification and Threading (Unchanged)

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (_disposed) return; // Don't raise events if disposed
            ThreadHelper.EnsureUiThread(() =>
            {
                 if (!_disposed) // Double-check after marshalling
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName); // Will check disposal state internally
            return true;
        }

        protected void RunOnUIThread(Action action) => ThreadHelper.EnsureUiThread(action);
        protected async Task RunOnUIThreadAsync(Action action) => await Task.Run(() => RunOnUIThread(action));
        protected async Task RunAsync(Func<Task> asyncOperation)
        {
            try { await asyncOperation(); }
            catch (Exception ex) { Debug.WriteLine($"Error in async operation: {ex}"); throw; }
        }
        protected async Task<T> RunWithResultAsync<T>(Func<T> operation) => await ThreadHelper.RunOnBackgroundThreadAsync(operation);

        #endregion

        #region Abstracted Command Creation Methods

        /// <summary>
        /// Creates a DelegateCommand, automatically observing the specified properties of this ViewModel
        /// and managing the command's lifetime.
        /// </summary>
        protected DelegateCommand CreateCommand(Action execute, Func<bool> canExecute = null, params string[] observedProperties)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            var command = new DelegateCommand(execute, canExecute);
            ObserveProperties(command, observedProperties);
            _ownedCommands.Add(command); // Manage lifetime
            return command;
        }

        /// <summary>
        /// Creates a typed DelegateCommand, automatically observing the specified properties of this ViewModel
        /// and managing the command's lifetime.
        /// </summary>
        protected DelegateCommand<T> CreateCommand<T>(Action<T> execute, Predicate<T> canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new DelegateCommand<T>(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command); // Manage lifetime
             return command;
        }

        /// <summary>
        /// Creates an AsyncDelegateCommand, automatically observing the specified properties of this ViewModel
        /// and managing the command's lifetime.
        /// </summary>
        protected AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new AsyncDelegateCommand(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command); // Manage lifetime
             return command;
        }

        /// <summary>
        /// Creates a typed AsyncDelegateCommand, automatically observing the specified properties of this ViewModel
        /// and managing the command's lifetime.
        /// </summary>
        protected AsyncDelegateCommand<T> CreateAsyncCommand<T>(Func<T, Task> execute, Predicate<T> canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             // Use the constructor that doesn't require explicit CancellationToken in derived VM
             var command = new AsyncDelegateCommand<T>(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command); // Manage lifetime
             return command;
        }

         /// <summary>
        /// Creates an AsyncRelayCommand (supporting cancellation), automatically observing the specified properties
        /// of this ViewModel and managing the command's lifetime.
        /// </summary>
        protected AsyncRelayCommand CreateCancellableAsyncCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new AsyncRelayCommand(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command); // Manage lifetime
             return command;
        }

        // Helper method to encapsulate observation logic
        private void ObserveProperties(ICommand command, string[] propertyNames)
        {
            if (propertyNames == null || propertyNames.Length == 0) return;

            // Use reflection or duck typing to call ObservesProperty if available
            // This avoids needing a specific interface on all command types JUST for this base class
            var observeMethod = command.GetType().GetMethod("ObservesProperty");
            if (observeMethod != null && observeMethod.GetParameters().Length == 2)
            {
                 Debug.WriteLine($"[ViewModelBase] Setting up observation for Command {command.GetHashCode()}");
                foreach (var propertyName in propertyNames)
                {
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        // Dynamically invoke: command.ObservesProperty(this, propertyName);
                        observeMethod.Invoke(command, new object[] { this, propertyName });
                    }
                }
            }
             else
             {
                 // Log a warning if a command type doesn't support observation as expected
                 Debug.WriteLine($"[ViewModelBase] Warning: Command type {command.GetType().Name} does not have an expected ObservesProperty(INotifyPropertyChanged, string) method. Automatic observation skipped.");
             }
        }

        #endregion

        #region Command Service Access (Unchanged)

        protected ICommand GetGlobalCommand(string commandName)
        {
            if (_commandService == null)
                 throw new InvalidOperationException("CommandService was not provided to this ViewModel.");
            return _commandService.GetCommand(commandName);
             // Consider returning null or a specific NullCommand implementation if not found
        }

        protected CompositeCommand GetGlobalCompositeCommand(string compositeName)
        {
             if (_commandService == null)
                 throw new InvalidOperationException("CommandService was not provided to this ViewModel.");
            return _commandService.GetCompositeCommand(compositeName);
             // Consider returning null if not found
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Releases resources used by the ViewModel, including disposing of owned commands.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Performs the actual resource cleanup.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // --- Clean up managed resources ---
                    Debug.WriteLine($"[ViewModelBase {GetType().Name}] Disposing. Cleaning up {_ownedCommands.Count} owned commands.");
                    // Dispose owned commands
                    foreach (var command in _ownedCommands)
                    {
                        try
                        {
                            command.Dispose();
                        }
                        catch(Exception ex)
                        {
                             Debug.WriteLine($"[ViewModelBase {GetType().Name}] Error disposing command {command.GetType().Name}: {ex.Message}");
                             // Log or handle disposal errors if necessary
                        }
                    }
                    _ownedCommands.Clear();

                    // Dispose other managed resources specific to derived classes by overriding this method
                    // --- End clean up managed resources ---
                }

                // Clean up unmanaged resources (if any) here

                _disposed = true;
                Debug.WriteLine($"[ViewModelBase {GetType().Name}] Disposed.");
            }
        }

        // Optional Finalizer (only needed if ViewModelBase directly owns unmanaged resources)
        // ~ViewModelBase()
        // {
        //     Dispose(false);
        // }

        #endregion
    }
}