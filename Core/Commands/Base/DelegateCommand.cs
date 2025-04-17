using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
    public class DelegateCommand<T> : IDelegateCommand<T>, IDisposable // Add IDisposable
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        // --- Observation Logic Fields ---
        private Dictionary<INotifyPropertyChanged, HashSet<string>> _observedPropertiesPerOwner;
        private readonly object _observerLock = new object();
        // --- End Observation Logic Fields ---

        // --- IDisposable Fields ---
        private bool _disposed = false;
        // --- End IDisposable Fields ---

        public event EventHandler CanExecuteChanged;

        public DelegateCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #region Command Execution Logic (Unchanged)
        public bool CanExecute(object parameter)
        {
            // Prevent execution if disposed
            if (_disposed) return false;

            if (_canExecute == null) return true;

            // Handle potential null parameters if T is a reference type or Nullable<T>
            if (parameter == null)
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    return false;
                return _canExecute(default(T));
            }

            // If parameter is not null, ensure it's assignable to T
            return parameter is T typedParameter && _canExecute(typedParameter);
        }

        public void Execute(object parameter)
        {
            // Re-check CanExecute before execution and check disposal state
            if (!CanExecute(parameter)) return;

            // Cast parameter safely
            T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                               ? default(T)
                               : (T)parameter;
            try
            {
                 _execute(typedParameter);
            }
            catch (Exception ex)
            {
                // Basic error logging or handling
                Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Exception during execution: {ex}");
                // Depending on requirements, you might want to re-throw or handle differently
                // throw;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            // Don't raise if disposed
            if (_disposed) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Double check disposal on UI thread in case of race condition
                if (!_disposed)
                {
                    //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] RaiseCanExecuteChanged invoked.");
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }
        #endregion

        #region Property Observation Logic (Unchanged Functionality, Added Disposal Check)
        /// <summary>
        /// Sets up property change observation to automatically raise CanExecuteChanged when specific properties change.
        /// Subscribes only once per owner object but tracks multiple properties per owner.
        /// </summary>
        /// <param name="owner">The INotifyPropertyChanged object that owns the property to observe.</param>
        /// <param name="propertyName">The name of the property to observe on the owner.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public DelegateCommand<T> ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            // Prevent observation if already disposed
            if (_disposed)
            {
                 //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Attempted to observe property on disposed command.");
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
                    //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Subscribed to PropertyChanged on Owner {owner.GetHashCode()}");
                }

                if (observedProperties.Add(propertyName))
                {
                     //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Observing property '{propertyName}' on Owner {owner.GetHashCode()}");
                }
            }
            return this;
        }

        private void Owner_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // If command is disposed, don't process property changes
            if (_disposed) return;

            var owner = sender as INotifyPropertyChanged;
            if (owner == null) return;

            HashSet<string> relevantProperties = null;
            bool shouldRaise = false;

            lock (_observerLock)
            {
                // Re-check disposal state after acquiring lock
                if (_disposed) return;

                if (_observedPropertiesPerOwner != null && _observedPropertiesPerOwner.TryGetValue(owner, out relevantProperties))
                {
                    if (string.IsNullOrEmpty(e.PropertyName) || relevantProperties.Contains(e.PropertyName))
                    {
                        shouldRaise = true;
                         //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Property '{e.PropertyName ?? "null"}' changed on Owner {owner.GetHashCode()}. Will raise CanExecuteChanged.");
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
        /// Releases resources used by the command, particularly PropertyChanged event subscriptions.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Prevent finalizer from running if already disposed
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
                    // --- Clean up managed resources (event subscriptions) ---
                    lock (_observerLock)
                    {
                        if (_observedPropertiesPerOwner != null)
                        {
                             //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Disposing: Unsubscribing from {_observedPropertiesPerOwner.Count} owners.");
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
                    // --- End clean up managed resources ---
                }

                // Clean up unmanaged resources (if any) here

                _disposed = true;
                 //Debug.WriteLine($"[DelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Disposed.");
            }
        }

        // Optional Finalizer (only needed if you have unmanaged resources)
        // ~DelegateCommand()
        // {
        //     Dispose(false);
        // }
        #endregion
    }

    /// <summary>
    /// A generic asynchronous command implementation that provides type safety for its parameters.
    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public class AsyncDelegateCommand<T> : IDelegateCommand<T>, IDisposable // Add IDisposable
    {
        private readonly Func<T, CancellationToken, Task> _execute;
        private readonly Predicate<T> _canExecute;
        private volatile bool _isExecuting;

        // --- Observation Logic Fields ---
        private Dictionary<INotifyPropertyChanged, HashSet<string>> _observedPropertiesPerOwner;
        private readonly object _observerLock = new object();
        // --- End Observation Logic Fields ---

        // --- IDisposable Fields ---
        private bool _disposed = false;
        // --- End IDisposable Fields ---


        public event EventHandler CanExecuteChanged;

        public AsyncDelegateCommand(Func<T, CancellationToken, Task> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncDelegateCommand(Func<T, Task> execute, Predicate<T> canExecute = null)
            : this((param, _) => execute(param), canExecute)
        {
        }

        #region Command Execution Logic (Unchanged)
         public bool CanExecute(object parameter)
        {
             if (_disposed) return false;
             if (_isExecuting) return false;
             if (_canExecute == null) return true;

             if (parameter == null)
             {
                 if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                     return false;
                 return _canExecute(default(T));
             }
             return parameter is T typedParameter && _canExecute(typedParameter);
        }

        public async void Execute(object parameter)
        {
             if (!CanExecute(parameter)) return;

             T typedParameter = (parameter == null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null))
                                ? default(T)
                                : (T)parameter;

             CancellationToken token = CancellationToken.None;

             try
             {
                 _isExecuting = true;
                 RaiseCanExecuteChanged();
                 await _execute(typedParameter, token);
             }
             catch(Exception ex)
             {
                  Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Exception during execution: {ex}");
                 // throw;
             }
             finally
             {
                 _isExecuting = false;
                 // Check disposal state before raising event in finally block
                 if(!_disposed) RaiseCanExecuteChanged();
             }
        }

        public void RaiseCanExecuteChanged()
        {
             if (_disposed) return;

             Application.Current?.Dispatcher?.Invoke(() =>
             {
                 if(!_disposed)
                 {
                    //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] RaiseCanExecuteChanged invoked.");
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                 }
             });
        }
        #endregion

        #region Property Observation Logic (Unchanged Functionality, Added Disposal Check)
        /// <summary>
        /// Sets up property change observation to automatically raise CanExecuteChanged when specific properties change.
        /// Subscribes only once per owner object but tracks multiple properties per owner.
        /// </summary>
        /// <param name="owner">The INotifyPropertyChanged object that owns the property to observe.</param>
        /// <param name="propertyName">The name of the property to observe on the owner.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public AsyncDelegateCommand<T> ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
             if (_disposed)
             {
                 //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Attempted to observe property on disposed command.");
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
                     //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Subscribed to PropertyChanged on Owner {owner.GetHashCode()}");
                 }

                 if (observedProperties.Add(propertyName))
                 {
                     //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Observing property '{propertyName}' on Owner {owner.GetHashCode()}");
                 }
             }
            return this;
        }

        private void Owner_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
             if (_disposed) return;

             var owner = sender as INotifyPropertyChanged;
             if (owner == null) return;

             HashSet<string> relevantProperties = null;
             bool shouldRaise = false;

             lock (_observerLock)
             {
                 if (_disposed) return;

                 if (_observedPropertiesPerOwner != null && _observedPropertiesPerOwner.TryGetValue(owner, out relevantProperties))
                 {
                     if (string.IsNullOrEmpty(e.PropertyName) || relevantProperties.Contains(e.PropertyName))
                     {
                         shouldRaise = true;
                          //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Property '{e.PropertyName ?? "null"}' changed on Owner {owner.GetHashCode()}. Will raise CanExecuteChanged.");
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
                             //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Disposing: Unsubscribing from {_observedPropertiesPerOwner.Count} owners.");
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
                 //Debug.WriteLine($"[AsyncDelegateCommand<{typeof(T).Name}> {this.GetHashCode()}] Disposed.");
            }
        }
        #endregion
    }

    // --- Non-Generic Classes Inherit IDisposable ---

    /// <summary>
    /// Non-generic delegate command to simplify command creation. Inherits IDisposable from base class.
    /// </summary>
    public class DelegateCommand : DelegateCommand<object>
    {
        public DelegateCommand(Action execute, Func<bool> canExecute = null)
            : base(_ => execute(), canExecute == null ? null : new Predicate<object>(_ => canExecute()))
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
    /// Non-generic asynchronous delegate command to simplify async command creation. Inherits IDisposable from base class.
    /// </summary>
    public class AsyncDelegateCommand : AsyncDelegateCommand<object>
    {
        public AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute = null)
            : base(_ => execute(), canExecute == null ? null : new Predicate<object>(_ => canExecute()))
        {
        }

         public AsyncDelegateCommand(Func<object, Task> execute, Func<object, bool> canExecute = null)
            : base( (param, _) => execute(param), canExecute == null ? null : new Predicate<object>(canExecute))
        {
        }

        public new AsyncDelegateCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            base.ObservesProperty(owner, propertyName);
            return this;
        }
    }
}