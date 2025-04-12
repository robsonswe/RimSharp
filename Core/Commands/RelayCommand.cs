using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RimSharp.Core.Commands
{
    /// <summary>
    /// A command whose sole purpose is to relay its functionality to other
    /// objects by invoking delegates.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(
                _ => execute(),
                canExecute == null ? null : _ => canExecute())
        {
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }


public class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool> _canExecute;

    public event EventHandler CanExecuteChanged;

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object parameter)
    {
        CancellationToken token = parameter is CancellationToken ct ? ct : CancellationToken.None;
        await _execute(token);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
    /// <summary>
    /// A generic command implementation that provides type safety for its parameters.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;
            return parameter is T typedParameter && _canExecute(typedParameter);
        }

        public void Execute(object parameter)
        {
            if (parameter is T typedParameter)
            {
                _execute(typedParameter);
            }
        }

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
/// A generic asynchronous command implementation that provides type safety for its parameters.
/// </summary>
public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, CancellationToken, Task> _execute;
    private readonly Predicate<T> _canExecute; // Or Func<T, bool>

    public event EventHandler CanExecuteChanged;

    public AsyncRelayCommand(Func<T, CancellationToken, Task> execute, Predicate<T> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter)
    {
        if (_canExecute == null) return true;

        // Handle potential null parameters if T is a reference type or Nullable<T>
        if (parameter == null)
        {
            // If T is a value type but not Nullable<T>, null is invalid.
            if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                return false;
            // Otherwise, null might be valid, pass default(T) which is null for reference/nullable types
            return _canExecute(default(T));
        }

        // If parameter is not null, ensure it's assignable to T
        return parameter is T typedParameter && _canExecute(typedParameter);
    }

    public async void Execute(object parameter)
    {
        // Re-check CanExecute before execution
        if (!CanExecute(parameter)) return;

        // Cast parameter safely. CanExecute should have already validated the type.
        T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                           ? default(T) // Handle valid null parameter
                           : (T)parameter;

        // AsyncRelayCommand (non-generic) passes CT, but here we don't automatically get one
        // from the parameter unless specifically designed for. Use CancellationToken.None.
        // If cancellation is needed, the Execute method itself must manage sourcing the token.
        await _execute(typedParameter, CancellationToken.None);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}


}