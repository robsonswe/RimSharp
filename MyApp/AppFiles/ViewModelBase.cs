using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;
using RimSharp.Core.Extensions;
using RimSharp.Core.Services.Commanding;

namespace RimSharp.MyApp.AppFiles
{
    /// <summary>
    /// Base class for all ViewModels in the application, providing property change notification
    /// and command support using the RimSharp Command System.
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        private readonly IModCommandService _commandService;

        /// <summary>
        /// Event that is raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewModelBase"/> class.
        /// </summary>
        public ViewModelBase()
        {
            // Default constructor for cases where command service isn't needed
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewModelBase"/> class with command service support.
        /// </summary>
        /// <param name="commandService">The command service for accessing global commands.</param>
        public ViewModelBase(IModCommandService commandService)
        {
            _commandService = commandService;
        }

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">Name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Use ThreadHelper for UI thread safety
            ThreadHelper.EnsureUiThread(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        /// <summary>
        /// Sets the property's value and raises PropertyChanged if the value has changed.
        /// </summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="field">Reference to the backing field.</param>
        /// <param name="value">New value for the property.</param>
        /// <param name="propertyName">Name of the property (auto-filled by compiler).</param>
        /// <returns>True if the value was changed, false if the new value equals the old value.</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Helper method for dispatching actions to UI thread.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        protected void RunOnUIThread(Action action)
        {
            ThreadHelper.EnsureUiThread(action);
        }

        /// <summary>
        /// Asynchronously executes an action on the UI thread.
        /// </summary>
        /// <param name="action">The action to execute on the UI thread.</param>
        protected async Task RunOnUIThreadAsync(Action action)
        {
            await Task.Run(() => RunOnUIThread(action));
        }

        /// <summary>
        /// Helper for safely running background operations that update the UI when complete.
        /// </summary>
        /// <param name="asyncOperation">The asynchronous operation to execute.</param>
        protected async Task RunAsync(Func<Task> asyncOperation)
        {
            try
            {
                await asyncOperation();
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error in async operation: {ex}");
                // Optionally re-throw or handle it depending on your error handling strategy
                throw;
            }
        }

        /// <summary>
        /// Helper for running CPU-intensive operations on a background thread.
        /// </summary>
        /// <typeparam name="T">The type of result returned by the operation.</typeparam>
        /// <param name="operation">The operation to execute on a background thread.</param>
        /// <returns>The result of the operation.</returns>
        protected async Task<T> RunWithResultAsync<T>(Func<T> operation)
        {
            return await ThreadHelper.RunOnBackgroundThreadAsync(operation);
        }

        #region Command Creation Methods

        /// <summary>
        /// Creates a DelegateCommand with the specified execute and canExecute delegates.
        /// </summary>
        /// <param name="execute">The action to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <returns>A new DelegateCommand instance.</returns>
        protected DelegateCommand CreateCommand(Action execute, Func<bool> canExecute = null)
        {
            return new DelegateCommand(execute, canExecute);
        }

        /// <summary>
        /// Creates a DelegateCommand with the specified execute and canExecute delegates, and sets up property observation.
        /// </summary>
        /// <param name="execute">The action to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <param name="propertyNames">Names of properties to observe for changes that may affect command execution.</param>
        /// <returns>A new DelegateCommand instance with property observation set up.</returns>
        protected DelegateCommand CreateCommand(Action execute, Func<bool> canExecute, params string[] propertyNames)
        {
            var command = new DelegateCommand(execute, canExecute);
            foreach (var propertyName in propertyNames)
            {
                command.ObservesProperty(this, propertyName);
            }
            return command;
        }

        /// <summary>
        /// Creates a typed DelegateCommand with the specified execute and canExecute delegates.
        /// </summary>
        /// <typeparam name="T">The type of parameter accepted by the command.</typeparam>
        /// <param name="execute">The action to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <returns>A new DelegateCommand instance.</returns>
        protected DelegateCommand<T> CreateCommand<T>(Action<T> execute, Predicate<T> canExecute = null)
        {
            return new DelegateCommand<T>(execute, canExecute);
        }

        /// <summary>
        /// Creates a typed DelegateCommand with the specified execute and canExecute delegates, and sets up property observation.
        /// </summary>
        /// <typeparam name="T">The type of parameter accepted by the command.</typeparam>
        /// <param name="execute">The action to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <param name="propertyNames">Names of properties to observe for changes that may affect command execution.</param>
        /// <returns>A new DelegateCommand instance with property observation set up.</returns>
        protected DelegateCommand<T> CreateCommand<T>(Action<T> execute, Predicate<T> canExecute, params string[] propertyNames)
        {
            var command = new DelegateCommand<T>(execute, canExecute);
            foreach (var propertyName in propertyNames)
            {
                command.ObservesProperty(this, propertyName);
            }
            return command;
        }

        /// <summary>
        /// Creates an AsyncDelegateCommand with the specified execute and canExecute delegates.
        /// </summary>
        /// <param name="execute">The async function to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <returns>A new AsyncDelegateCommand instance.</returns>
        protected AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            return new AsyncDelegateCommand(execute, canExecute);
        }

        /// <summary>
        /// Creates an AsyncDelegateCommand with the specified execute and canExecute delegates, and sets up property observation.
        /// </summary>
        /// <param name="execute">The async function to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <param name="propertyNames">Names of properties to observe for changes that may affect command execution.</param>
        /// <returns>A new AsyncDelegateCommand instance with property observation set up.</returns>
        protected AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute, params string[] propertyNames)
        {
            var command = new AsyncDelegateCommand(execute, canExecute);
            foreach (var propertyName in propertyNames)
            {
                command.ObservesProperty(this, propertyName);
            }
            return command;
        }

        /// <summary>
        /// Creates a typed AsyncDelegateCommand with the specified execute and canExecute delegates.
        /// </summary>
        /// <typeparam name="T">The type of parameter accepted by the command.</typeparam>
        /// <param name="execute">The async function to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <returns>A new AsyncDelegateCommand instance.</returns>
        protected AsyncDelegateCommand<T> CreateAsyncCommand<T>(Func<T, Task> execute, Predicate<T> canExecute = null)
        {
            return new AsyncDelegateCommand<T>(execute, canExecute);
        }

        /// <summary>
        /// Creates a typed AsyncDelegateCommand with the specified execute and canExecute delegates, and sets up property observation.
        /// </summary>
        /// <typeparam name="T">The type of parameter accepted by the command.</typeparam>
        /// <param name="execute">The async function to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <param name="propertyNames">Names of properties to observe for changes that may affect command execution.</param>
        /// <returns>A new AsyncDelegateCommand instance with property observation set up.</returns>
        protected AsyncDelegateCommand<T> CreateAsyncCommand<T>(Func<T, Task> execute, Predicate<T> canExecute, params string[] propertyNames)
        {
            var command = new AsyncDelegateCommand<T>(execute, canExecute);
            foreach (var propertyName in propertyNames)
            {
                command.ObservesProperty(this, propertyName);
            }
            return command;
        }

        /// <summary>
        /// Creates an AsyncDelegateCommand with cancellation support.
        /// </summary>
        /// <param name="execute">The async function to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <returns>A new AsyncDelegateCommand instance with cancellation support.</returns>
        protected AsyncRelayCommand CreateCancellableAsyncCommand(Func<System.Threading.CancellationToken, Task> execute, Func<bool> canExecute = null)
        {
            return new AsyncRelayCommand(execute, canExecute);
        }

        /// <summary>
        /// Creates an AsyncDelegateCommand with cancellation support and property observation.
        /// </summary>
        /// <param name="execute">The async function to execute when the command is invoked.</param>
        /// <param name="canExecute">Optional predicate that determines whether the command can execute.</param>
        /// <param name="propertyNames">Names of properties to observe for changes that may affect command execution.</param>
        /// <returns>A new AsyncDelegateCommand instance with cancellation support and property observation.</returns>
        protected AsyncRelayCommand CreateCancellableAsyncCommand(Func<System.Threading.CancellationToken, Task> execute, Func<bool> canExecute, params string[] propertyNames)
        {
            var command = new AsyncRelayCommand(execute, canExecute);
            foreach (var propertyName in propertyNames)
            {
                command.ObservesProperty(this, propertyName);
            }
            return command;
        }

        #endregion

        #region Command Service Access

        /// <summary>
        /// Gets a command from the command service by name.
        /// </summary>
        /// <param name="commandName">The name of the command to retrieve.</param>
        /// <returns>The command if found; otherwise, null.</returns>
        protected ICommand GetCommand(string commandName)
        {
            if (_commandService == null || !_commandService.ContainsCommand(commandName))
                return null;

            return _commandService.GetCommand(commandName);
        }

        /// <summary>
        /// Gets a composite command from the command service by name.
        /// </summary>
        /// <param name="compositeName">The name of the composite command to retrieve.</param>
        /// <returns>The composite command if found; otherwise, null.</returns>
        protected ICommand GetCompositeCommand(string compositeName)
        {
            if (_commandService == null)
                return null;

            return _commandService.GetCompositeCommand(compositeName);
        }

        #endregion
    }
}


