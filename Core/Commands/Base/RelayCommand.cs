using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RimSharp.Core.Commands.Base
{
    /// <summary>
    /// A command implementation that relays functionality to delegate methods.
    /// </summary>
    public class RelayCommand : IDelegateCommand, IDisposable // Add IDisposable
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        // --- Observation Logic Fields ---
        private Dictionary<INotifyPropertyChanged, HashSet<string>> _observedPropertiesPerOwner;
        private readonly object _observerLock = new object();
        // --- End Observation Logic Fields ---

        // --- IDisposable Fields ---
        private bool _disposed = false;
        // --- End IDisposable Fields ---

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute())
        {
        }

        #region Command Execution Logic (Unchanged)
        public bool CanExecute(object parameter)
        {
            if (_disposed) return false;
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return; // Re-check before execution
            try
            {
                 _execute(parameter);
            }
             catch (Exception ex)
            {
                Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Exception during execution: {ex}");
                // throw;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            if (_disposed) return;

            RimSharp.Core.Extensions.ThreadHelper.EnsureUiThread(() =>
            {
                if(!_disposed)
                {
                     //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] RaiseCanExecuteChanged invoked.");
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
        public RelayCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
            if (_disposed)
            {
                 //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Attempted to observe property on disposed command.");
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
                    //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Subscribed to PropertyChanged on Owner {owner.GetHashCode()}");
                }

                if(observedProperties.Add(propertyName))
                {
                    //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Observing property '{propertyName}' on Owner {owner.GetHashCode()}");
                }
            }
            return this;
        }

        /// <summary>
        /// Convenience method to observe multiple properties on the same owner.
        /// </summary>
        /// <param name="owner">The object that owns the properties to observe.</param>
        /// <param name="propertyNames">The names of the properties to observe.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public RelayCommand ObservesProperties(INotifyPropertyChanged owner, params string[] propertyNames)
        {
             // Check disposal state first
            if (_disposed)
            {
                 //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Attempted to observe properties on disposed command.");
                 return this;
            }

            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (propertyNames == null || propertyNames.Length == 0) throw new ArgumentNullException(nameof(propertyNames));

            // Reuse the single property logic within the lock for safety
            lock(_observerLock)
            {
                if (_disposed) return this; // Re-check inside lock

                foreach (var propertyName in propertyNames)
                {
                    if (!string.IsNullOrEmpty(propertyName))
                    {
                        // Internal call avoids repeated locking and null checks
                        ObservesPropertyInternal(owner, propertyName);
                    }
                }
            }
            return this;
        }

        // Private helper to avoid repeated checks/locking in ObservesProperties loop
        private void ObservesPropertyInternal(INotifyPropertyChanged owner, string propertyName)
        {
            // Assumes lock is already held and owner/propertyName are valid
             if (_observedPropertiesPerOwner == null)
            {
                _observedPropertiesPerOwner = new Dictionary<INotifyPropertyChanged, HashSet<string>>();
            }

            if (!_observedPropertiesPerOwner.TryGetValue(owner, out var observedProperties))
            {
                observedProperties = new HashSet<string>();
                _observedPropertiesPerOwner.Add(owner, observedProperties);
                owner.PropertyChanged += Owner_PropertyChanged;
                //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Subscribed to PropertyChanged on Owner {owner.GetHashCode()}");
            }

            if(observedProperties.Add(propertyName))
            {
                //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Observing property '{propertyName}' on Owner {owner.GetHashCode()}");
            }
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
                          //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Property '{e.PropertyName ?? "null"}' changed on Owner {owner.GetHashCode()}. Will raise CanExecuteChanged.");
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
                             //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Disposing: Unsubscribing from {_observedPropertiesPerOwner.Count} owners.");
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
                //Debug.WriteLine($"[RelayCommand {this.GetHashCode()}] Disposed.");
            }
        }
        #endregion
    }

    /// <summary>
    /// An asynchronous command implementation that relays functionality to delegate methods, supporting cancellation.
    /// </summary>
    public class AsyncRelayCommand : IDelegateCommand, IDisposable // Add IDisposable
    {
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<bool> _canExecute;
        private volatile bool _isExecuting;

        // --- Observation Logic Fields ---
        private Dictionary<INotifyPropertyChanged, HashSet<string>> _observedPropertiesPerOwner;
        private readonly object _observerLock = new object();
        // --- End Observation Logic Fields ---

        // --- IDisposable Fields ---
        private bool _disposed = false;
        // --- End IDisposable Fields ---


        public event EventHandler CanExecuteChanged;

        public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
            : this(ct => execute(), canExecute)
        {
        }

        #region Command Execution Logic (Unchanged)
        public bool CanExecute(object parameter)
        {
            if(_disposed) return false;
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object parameter)
        {
            CancellationToken token = parameter is CancellationToken ct ? ct : CancellationToken.None;

            if (!CanExecute(parameter)) return;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _execute(token);
            }
            catch(Exception ex)
            {
                 Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Exception during execution: {ex}");
                 // throw;
            }
            finally
            {
                _isExecuting = false;
                 if(!_disposed) RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
             if (_disposed) return;

             RimSharp.Core.Extensions.ThreadHelper.EnsureUiThread(() =>
             {
                  if(!_disposed)
                  {
                    //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] RaiseCanExecuteChanged invoked.");
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
        public AsyncRelayCommand ObservesProperty(INotifyPropertyChanged owner, string propertyName)
        {
             if (_disposed)
             {
                 //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Attempted to observe property on disposed command.");
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
                     //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Subscribed to PropertyChanged on Owner {owner.GetHashCode()}");
                 }

                 if(observedProperties.Add(propertyName))
                 {
                    //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Observing property '{propertyName}' on Owner {owner.GetHashCode()}");
                 }
             }
            return this;
        }

         /// <summary>
        /// Convenience method to observe multiple properties on the same owner.
        /// </summary>
        /// <param name="owner">The object that owns the properties to observe.</param>
        /// <param name="propertyNames">The names of the properties to observe.</param>
        /// <returns>The command instance to allow for method chaining.</returns>
        public AsyncRelayCommand ObservesProperties(INotifyPropertyChanged owner, params string[] propertyNames)
        {
             if (_disposed)
             {
                 //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Attempted to observe properties on disposed command.");
                 return this;
             }

             if (owner == null) throw new ArgumentNullException(nameof(owner));
             if (propertyNames == null || propertyNames.Length == 0) throw new ArgumentNullException(nameof(propertyNames));

            // Reuse the single property logic within the lock for safety
            lock(_observerLock)
            {
                 if (_disposed) return this;

                foreach (var propertyName in propertyNames)
                {
                     if (!string.IsNullOrEmpty(propertyName))
                     {
                         ObservesPropertyInternal(owner, propertyName);
                     }
                }
            }
            return this;
        }

        // Private helper to avoid repeated checks/locking in ObservesProperties loop
        private void ObservesPropertyInternal(INotifyPropertyChanged owner, string propertyName)
        {
            // Assumes lock is already held and owner/propertyName are valid
            if (_observedPropertiesPerOwner == null)
            {
                _observedPropertiesPerOwner = new Dictionary<INotifyPropertyChanged, HashSet<string>>();
            }

            if (!_observedPropertiesPerOwner.TryGetValue(owner, out var observedProperties))
            {
                observedProperties = new HashSet<string>();
                _observedPropertiesPerOwner.Add(owner, observedProperties);
                owner.PropertyChanged += Owner_PropertyChanged;
                //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Subscribed to PropertyChanged on Owner {owner.GetHashCode()}");
            }

            if(observedProperties.Add(propertyName))
            {
                //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Observing property '{propertyName}' on Owner {owner.GetHashCode()}");
            }
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
                          //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Property '{e.PropertyName ?? "null"}' changed on Owner {owner.GetHashCode()}. Will raise CanExecuteChanged.");
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
                             //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Disposing: Unsubscribing from {_observedPropertiesPerOwner.Count} owners.");
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
                 //Debug.WriteLine($"[AsyncRelayCommand {this.GetHashCode()}] Disposed.");
            }
        }
        #endregion
    }
}