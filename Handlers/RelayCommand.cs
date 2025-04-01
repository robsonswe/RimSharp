// Update RelayCommand.cs
using System;
using System.Windows.Input;

namespace RimSharp.Handlers
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Action _executeNoParam;
        private readonly Func<object, bool> _canExecute;
        
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        
        // Constructor for commands with parameters
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        
        // New constructor for parameterless commands
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _executeNoParam = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute != null ? _ => canExecute() : (Func<object, bool>)null;
        }
        
        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;
            return _canExecute(parameter);
        }
        
        public void Execute(object parameter)
        {
            if (_executeNoParam != null)
            {
                _executeNoParam();
            }
            else
            {
                _execute(parameter);
            }
        }
    }
}