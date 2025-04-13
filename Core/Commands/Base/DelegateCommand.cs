using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace RimSharp.Core.Commands.Base
{
    /// <summary>
    /// A generic command implementation that provides type safety for its parameters.
    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public class DelegateCommand<T> : IDelegateCommand<T>
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;
        private readonly List<INotifyPropertyChanged> _observedProperties = new List<INotifyPropertyChanged>();

        public event EventHandler CanExecuteChanged;

        public DelegateCommand(Action<T> execute, Predicate<T> canExecute = null)
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

        public void Execute(object parameter)
        {
            // Re-check CanExecute before execution
            if (!CanExecute(parameter)) return;

            // Cast parameter safely
            T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                               ? default(T) // Handle valid null parameter
                               : (T)parameter;

            _execute(typedParameter);
        }

        public void RaiseCanExecuteChanged() =>
            Application.Current?.Dispatcher?.Invoke(() =>
                CanExecuteChanged?.Invoke(this, EventArgs.Empty));

        /// <summary>
        /// Sets up property change observation to automatically raise CanExecuteChanged when a property changes.
        /// </summary>
        /// <param name="owner">The object that owns the property to observe.</param>
        /// <param name="propertyName">The name of the property to observe.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public DelegateCommand<T> ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            if (!_observedProperties.Contains(owner))
            {
                _observedProperties.Add(owner);
                owner.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == propertyName || string.IsNullOrEmpty(args.PropertyName))
                    {
                        RaiseCanExecuteChanged();
                    }
                };
            }

            return this;
        }
    }

    /// <summary>
    /// A generic asynchronous command implementation that provides type safety for its parameters.
    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public class AsyncDelegateCommand<T> : IDelegateCommand<T>
    {
        private readonly Func<T, CancellationToken, Task> _execute;
        private readonly Predicate<T> _canExecute;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncDelegateCommand(Func<T, CancellationToken, Task> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncDelegateCommand(Func<T, Task> execute, Predicate<T> canExecute = null)
            : this(
                (param, _) => execute(param),
                canExecute)
        {
        }

        public bool CanExecute(object parameter)
        {
            if (_isExecuting) return false;
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

            // Cast parameter safely
            T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                               ? default(T) // Handle valid null parameter
                               : (T)parameter;

            // Get cancellation token if provided, otherwise use None
            CancellationToken token = CancellationToken.None;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _execute(typedParameter, token);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() =>
            Application.Current?.Dispatcher?.Invoke(() =>
                CanExecuteChanged?.Invoke(this, EventArgs.Empty));

        /// <summary>
        /// Sets up property change observation to automatically raise CanExecuteChanged when a property changes.
        /// </summary>
        /// <param name="owner">The object that owns the property to observe.</param>
        /// <param name="propertyName">The name of the property to observe.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public AsyncDelegateCommand<T> ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            owner.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == propertyName || string.IsNullOrEmpty(args.PropertyName))
                {
                    RaiseCanExecuteChanged();
                }
            };

            return this;
        }
    }

    /// <summary>
    /// Non-generic delegate command to simplify command creation.
    /// </summary>
    public class DelegateCommand : DelegateCommand<object>
    {
        public DelegateCommand(Action execute, Func<bool> canExecute = null)
            : base(
                _ => execute(),
                canExecute == null ? null : _ => canExecute())
        {
        }

        public DelegateCommand(Action<object> execute, Func<object, bool> canExecute = null)
            : base(execute, canExecute == null ? null : new Predicate<object>(canExecute))
        {
        }

        public new DelegateCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            base.ObservesProperty(owner, propertyName);
            return this;
        }
    }

    /// <summary>
    /// Non-generic asynchronous delegate command to simplify async command creation.
    /// </summary>
    public class AsyncDelegateCommand : AsyncDelegateCommand<object>
    {
        public AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute = null)
            : base(
                _ => execute(),
                canExecute == null ? null : _ => canExecute())
        {
        }

        public AsyncDelegateCommand(Func<object, Task> execute, Func<object, bool> canExecute = null)
            : base((param, _) => execute(param),
                   canExecute == null ? null : new Predicate<object>(canExecute))
        {
        }

        public new AsyncDelegateCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            base.ObservesProperty(owner, propertyName);
            return this;
        }
    }
}