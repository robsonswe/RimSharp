using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RimSharp.Core.Commands.Base
{
    /// <summary>
    /// A command implementation that relays functionality to delegate methods.
    /// </summary>
    public class RelayCommand : IDelegateCommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

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

        public void RaiseCanExecuteChanged() => 
            Application.Current?.Dispatcher?.Invoke(() => 
                CanExecuteChanged?.Invoke(this, EventArgs.Empty));

        /// <summary>
        /// Sets up property change observation to automatically raise CanExecuteChanged when a property changes.
        /// </summary>
        /// <param name="owner">The object that owns the property to observe.</param>
        /// <param name="propertyName">The name of the property to observe.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public RelayCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
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

        /// <summary>
        /// Sets up property change observation to automatically raise CanExecuteChanged when multiple properties change.
        /// </summary>
        /// <param name="owner">The object that owns the properties to observe.</param>
        /// <param name="propertyNames">The names of the properties to observe.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public RelayCommand ObservesProperties(INotifyPropertyChanged owner, params string[] propertyNames)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (propertyNames == null || propertyNames.Length == 0) throw new ArgumentNullException(nameof(propertyNames));

            owner.PropertyChanged += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.PropertyName) || Array.IndexOf(propertyNames, args.PropertyName) >= 0)
                {
                    RaiseCanExecuteChanged();
                }
            };

            return this;
        }
    }

    /// <summary>
    /// An asynchronous command implementation that relays functionality to delegate methods.
    /// </summary>
    public class AsyncRelayCommand : IDelegateCommand
    {
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
            : this(
                ct => execute(),
                canExecute)
        {
        }

        public bool CanExecute(object parameter) => 
            !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object parameter)
        {
            CancellationToken token = parameter is CancellationToken ct ? ct : CancellationToken.None;
            
            if (!CanExecute(parameter))
                return;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _execute(token);
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
        public AsyncRelayCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
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
}