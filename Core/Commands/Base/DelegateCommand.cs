using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RimSharp.Core.Commands.Base
{
    /// <summary>

    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public class DelegateCommand<T> : IDelegateCommand<T>, IDisposable 
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T>? _canExecute;
        private Dictionary<INotifyPropertyChanged, HashSet<string>>? _observedPropertiesPerOwner;
        private readonly object _observerLock = new object();
        private bool _disposed = false;

        public event EventHandler? CanExecuteChanged;

        public DelegateCommand(Action<T> execute, Predicate<T>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #region Command Execution Logic (Unchanged)
        public bool CanExecute(object? parameter)
        {
            // Prevent execution if disposed
            if (_disposed) return false;

            if (_canExecute == null) return true;

            if (parameter == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    return false;
                return _canExecute(default(T)!);
            }

            return parameter is T typedParameter && _canExecute(typedParameter);
        }

        public void Execute(object? parameter)
        {
            // Re-check CanExecute before execution and check disposal state
            if (!CanExecute(parameter)) return;

            // Cast parameter safely
            T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                               ? default(T)!
                               : (T)parameter!;
            try
            {
                 _execute(typedParameter);
            }
            catch (Exception ex)
            {
                // Basic error logging or handling
                Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Exception during execution: {ex}");

                // throw;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            // Don't raise if disposed
            if (_disposed) return;

            RimSharp.Core.Extensions.ThreadHelper.EnsureUiThread(() =>
            {

                if (!_disposed)
                {

                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }
        #endregion

        #region Property Observation Logic (Unchanged Functionality, Added Disposal Check)
        /// <summary>

/// </summary>

/// <returns>The command instance to allow for method chaining.</returns>
        public DelegateCommand<T> ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            // Prevent observation if already disposed
            if (_disposed)
            {

                 return this; // Or throw ObjectDisposedException
            }

            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

            lock (_observerLock)
            {
                 // Re-check disposal state after acquiring lock
                 if (_disposed) return this;

                if (_observedPropertiesPerOwner == null)
                {
                    _observedPropertiesPerOwner = new Dictionary<INotifyPropertyChanged, HashSet<string>>();
                }

                if (!_observedPropertiesPerOwner.TryGetValue(owner, out var observedProperties))
                {
                    observedProperties = new HashSet<string>();
                    _observedPropertiesPerOwner.Add(owner, observedProperties);
                    owner.PropertyChanged += Owner_PropertyChanged;

                }

                if (observedProperties.Add(propertyName))
                {

                }
            }
            return this;
        }

        private void Owner_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {

            if (_disposed) return;

            var owner = sender as INotifyPropertyChanged;
            if (owner == null) return;

            HashSet<string>? relevantProperties = null;
            bool shouldRaise = false;

            lock (_observerLock)
            {
                // Re-check disposal state after acquiring lock
                if (_disposed) return;

                if (_observedPropertiesPerOwner != null && _observedPropertiesPerOwner.TryGetValue(owner, out relevantProperties))
                {
                    if (string.IsNullOrEmpty(e.PropertyName) || (relevantProperties != null && relevantProperties.Contains(e.PropertyName)))
                    {
                        shouldRaise = true;

                    }
                }
            }

            if (shouldRaise)
            {
                RaiseCanExecuteChanged();
            }
        }
        #endregion

        #region IDisposable Implementation
        /// <summary>

        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Prevent finalizer from running if already disposed
        }

        /// <summary>
        /// Performs the actual resource cleanup.
        /// </summary>

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    lock (_observerLock)
                    {
                        if (_observedPropertiesPerOwner != null)
                        {

                            foreach (var owner in _observedPropertiesPerOwner.Keys)
                            {
                                if (owner != null) // Safety check
                                {
                                    owner.PropertyChanged -= Owner_PropertyChanged;
                                }
                            }
                            _observedPropertiesPerOwner.Clear();
                            _observedPropertiesPerOwner = null; // Help GC
                        }
                    }
                }

                // Clean up unmanaged resources (if any) here

                _disposed = true;

            }
        }

        // ~DelegateCommand()
        // {

        #endregion
    }

    /// <summary>

    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public class AsyncDelegateCommand<T> : IDelegateCommand<T>, IDisposable 
    {
        private readonly Func<T, CancellationToken, Task> _execute;
        private readonly Predicate<T>? _canExecute;
        private volatile bool _isExecuting;
        private Dictionary<INotifyPropertyChanged, HashSet<string>>? _observedPropertiesPerOwner;
        private readonly object _observerLock = new object();
        private bool _disposed = false;

public event EventHandler? CanExecuteChanged;

        public AsyncDelegateCommand(Func<T, CancellationToken, Task> execute, Predicate<T>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncDelegateCommand(Func<T, Task> execute, Predicate<T>? canExecute = null)
            : this((param, _) => execute(param), canExecute)
        {
        }

        #region Command Execution Logic (Unchanged)
         public bool CanExecute(object? parameter)
        {
             if (_disposed) return false;
             if (_isExecuting) return false;
             if (_canExecute == null) return true;

             if (parameter == null)
             {
                 if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                     return false;
                 return _canExecute(default(T)!);
             }
             return parameter is T typedParameter && _canExecute(typedParameter);
        }

        public async void Execute(object? parameter)
        {
             if (!CanExecute(parameter)) return;

             T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                                ? default(T)!
                                : (T)parameter!;

             await ExecuteAsync(typedParameter);
        }

        public async Task ExecuteAsync(T parameter)
        {
            CancellationToken token = CancellationToken.None;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _execute(parameter, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Exception during execution: {ex}");
                // throw;
            }
            finally
            {
                _isExecuting = false;
                if (!_disposed) RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
             if (_disposed) return;

             RimSharp.Core.Extensions.ThreadHelper.EnsureUiThread(() =>
             {
                 if(!_disposed)
                 {

                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                 }
             });
        }
        #endregion

        #region Property Observation Logic (Unchanged Functionality, Added Disposal Check)
        /// <summary>

/// </summary>

/// <returns>The command instance to allow for method chaining.</returns>
        public AsyncDelegateCommand<T> ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
             if (_disposed)
             {

                 return this;
             }

             if (owner == null) throw new ArgumentNullException(nameof(owner));
             if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

             lock (_observerLock)
             {
                 if (_disposed) return this;

                 if (_observedPropertiesPerOwner == null)
                 {
                     _observedPropertiesPerOwner = new Dictionary<INotifyPropertyChanged, HashSet<string>>();
                 }

                 if (!_observedPropertiesPerOwner.TryGetValue(owner, out var observedProperties))
                 {
                     observedProperties = new HashSet<string>();
                     _observedPropertiesPerOwner.Add(owner, observedProperties);
                     owner.PropertyChanged += Owner_PropertyChanged;

                 }

                 if (observedProperties.Add(propertyName))
                 {

                 }
             }
            return this;
        }

        private void Owner_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
             if (_disposed) return;

             var owner = sender as INotifyPropertyChanged;
             if (owner == null) return;

             HashSet<string>? relevantProperties = null;
             bool shouldRaise = false;

             lock (_observerLock)
             {
                 if (_disposed) return;

                 if (_observedPropertiesPerOwner != null && _observedPropertiesPerOwner.TryGetValue(owner, out relevantProperties))
                 {
                     if (string.IsNullOrEmpty(e.PropertyName) || (relevantProperties != null && relevantProperties.Contains(e.PropertyName)))
                     {
                         shouldRaise = true;

                     }
                 }
             }

             if (shouldRaise)
             {
                 RaiseCanExecuteChanged();
             }
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
                    lock (_observerLock)
                    {
                        if (_observedPropertiesPerOwner != null)
                        {

                            foreach (var owner in _observedPropertiesPerOwner.Keys)
                            {
                                if (owner != null) owner.PropertyChanged -= Owner_PropertyChanged;
                            }
                            _observedPropertiesPerOwner.Clear();
                            _observedPropertiesPerOwner = null;
                        }
                    }
                }
                _disposed = true;

            }
        }
        #endregion
    }

    /// <summary>

    /// </summary>
    public class DelegateCommand : DelegateCommand<object>
    {
        public DelegateCommand(Action execute, Func<bool>? canExecute = null)
            : base(_ => execute(), canExecute == null ? null : new Predicate<object>(_ => canExecute()!))
        {
        }

        public DelegateCommand(Action<object> execute, Func<object, bool>? canExecute = null)
            : base(execute, canExecute == null ? null : new Predicate<object>(canExecute!))
        {
        }

        public new DelegateCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            base.ObservesProperty(owner, propertyName);
            return this;
        }
    }

    /// <summary>

    /// </summary>
    public class AsyncDelegateCommand : AsyncDelegateCommand<object>
    {
        public AsyncDelegateCommand(Func<Task> execute, Func<bool>? canExecute = null)
            : base(_ => execute(), canExecute == null ? null : new Predicate<object>(_ => canExecute()!))
        {
        }

         public AsyncDelegateCommand(Func<object, Task> execute, Func<object, bool>? canExecute = null)
            : base( (param, _) => execute(param), canExecute == null ? null : new Predicate<object>(canExecute!))
        {
        }

        public Task ExecuteAsync() => base.ExecuteAsync(null!);

        public new AsyncDelegateCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            base.ObservesProperty(owner, propertyName);
            return this;
        }
    }
}


