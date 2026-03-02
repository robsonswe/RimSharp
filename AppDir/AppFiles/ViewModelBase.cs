using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;
using RimSharp.Core.Commands.Composite;
using RimSharp.Core.Extensions;
using RimSharp.Core.Services.Commanding;
using ReactiveUI;

namespace RimSharp.AppDir.AppFiles
{
    public abstract class ViewModelBase : ReactiveObject, IDisposable
    {
        private readonly IModCommandService? _commandService;
        private readonly List<IDisposable> _ownedCommands = new List<IDisposable>();
        protected bool _disposed = false;

        protected ViewModelBase() { }

        protected ViewModelBase(IModCommandService commandService)
        {
            _commandService = commandService;
        }

        #region Property Notification and Threading

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (_disposed) return;
            (this as IReactiveObject).RaisePropertyChanged(new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            this.RaiseAndSetIfChanged(ref field, value, propertyName);
            return true;
        }

        protected void RunOnUIThread(Action action) => ThreadHelper.EnsureUiThread(action);
        protected void RunOnUIThread(Func<Task> asyncFunc) => _ = ThreadHelper.RunOnUIThreadAsync(asyncFunc);
        protected async Task RunOnUIThreadAsync(Action action) => await ThreadHelper.RunOnUIThreadAsync(action);
        protected async Task RunOnUIThreadAsync(Func<Task> asyncFunc) => await ThreadHelper.RunOnUIThreadAsync(asyncFunc);
        protected async Task<T?> RunOnUIThreadAsync<T>(Func<T> func) => await ThreadHelper.RunOnUIThreadAsync(func)!;
        
        protected async Task RunAsync(Func<Task> asyncOperation)
        {
            try { await asyncOperation(); }
            catch (Exception ex) { Debug.WriteLine($"Error in async operation: {ex}"); throw; }
        }
        
        protected async Task<T?> RunWithResultAsync<T>(Func<T> operation) => await ThreadHelper.RunOnBackgroundThreadAsync(operation)!;

        #endregion

        #region Command Creation Methods

        protected DelegateCommand CreateCommand(Action execute, Func<bool>? canExecute = null, params string[] observedProperties)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            var command = new DelegateCommand(execute, canExecute!);
            ObserveProperties(command, observedProperties);
            _ownedCommands.Add(command);
            return command;
        }

        protected DelegateCommand<T> CreateCommand<T>(Action<T> execute, Predicate<T>? canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new DelegateCommand<T>(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command);
             return command;
        }

        protected AsyncDelegateCommand CreateAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new AsyncDelegateCommand(execute, canExecute!);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command);
             return command;
        }

        protected AsyncDelegateCommand<T> CreateAsyncCommand<T>(Func<T, Task> execute, Predicate<T>? canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new AsyncDelegateCommand<T>(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command);
             return command;
        }

        protected AsyncRelayCommand CreateCancellableAsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null, params string[] observedProperties)
        {
             if (_disposed) throw new ObjectDisposedException(GetType().Name);
             var command = new AsyncRelayCommand(execute, canExecute);
             ObserveProperties(command, observedProperties);
             _ownedCommands.Add(command);
             return command;
        }

        private void ObserveProperties(ICommand command, string[] propertyNames)
        {
            if (propertyNames == null || propertyNames.Length == 0) return;

            var observeMethod = command.GetType().GetMethod("ObservesProperty");
            if (observeMethod != null && observeMethod.GetParameters().Length == 2)
            {
                foreach (var propertyName in propertyNames)
                {
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        observeMethod.Invoke(command, new object[] { this, propertyName });
                    }
                }
            }
        }

        #endregion

        #region Command Service Access

        protected ICommand? GetGlobalCommand(string commandName)
        {
            if (_commandService == null)
                 throw new InvalidOperationException("CommandService was not provided to this ViewModel.");
            return _commandService.GetCommand(commandName);
        }

        protected CompositeCommand? GetGlobalCompositeCommand(string compositeName)
        {
             if (_commandService == null)
                 throw new InvalidOperationException("CommandService was not provided to this ViewModel.");
            return _commandService.GetCompositeCommand(compositeName);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var command in _ownedCommands)
                    {
                        try { command.Dispose(); }
                        catch(Exception ex) { Debug.WriteLine($"Error disposing command: {ex.Message}"); }
                    }
                    _ownedCommands.Clear();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
