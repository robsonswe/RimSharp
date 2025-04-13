using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;

namespace RimSharp.Core.Commands.Composite
{
    /// <summary>
    /// A command that aggregates multiple commands and executes them as one.
    /// </summary>
    public class CompositeCommand : IDelegateCommand
    {
        private readonly List<ICommand> _commands = new List<ICommand>();
        private readonly object _lock = new object();
        private readonly bool _monitorCommandActivity;

        public event EventHandler CanExecuteChanged;
        
        /// <summary>
        /// Gets the list of registered commands.
        /// </summary>
        public IReadOnlyList<ICommand> RegisteredCommands => _commands.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeCommand"/> class.
        /// </summary>
        /// <param name="monitorCommandActivity">
        /// If true, each command will be monitored for its CanExecuteChanged event.
        /// When any command's CanExecuteChanged fires, the CompositeCommand's CanExecuteChanged will fire too.
        /// </param>
        public CompositeCommand(bool monitorCommandActivity = true)
        {
            _monitorCommandActivity = monitorCommandActivity;
        }

        /// <summary>
        /// Registers a command with the composite command.
        /// </summary>
        /// <param name="command">The command to register.</param>
        /// <exception cref="ArgumentNullException">Thrown if command is null.</exception>
        public void RegisterCommand(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            
            lock (_lock)
            {
                if (!_commands.Contains(command))
                {
                    _commands.Add(command);
                    
                    if (_monitorCommandActivity)
                    {
                        command.CanExecuteChanged += CommandCanExecuteChanged;
                    }
                }
            }
        }

        /// <summary>
        /// Unregisters a command from the composite command.
        /// </summary>
        /// <param name="command">The command to unregister.</param>
        public void UnregisterCommand(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            
            lock (_lock)
            {
                if (_commands.Contains(command))
                {
                    _commands.Remove(command);
                    
                    if (_monitorCommandActivity)
                    {
                        command.CanExecuteChanged -= CommandCanExecuteChanged;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if the composite command can execute.
        /// </summary>
        /// <param name="parameter">The parameter to pass to the commands.</param>
        /// <returns>True if all commands can execute; otherwise, false.</returns>
        public bool CanExecute(object parameter)
        {
            if (!_commands.Any())
            {
                return false;
            }

            lock (_lock)
            {
                return _commands.All(command => command.CanExecute(parameter));
            }
        }

        /// <summary>
        /// Executes all commands that can execute.
        /// </summary>
        /// <param name="parameter">The parameter to pass to the commands.</param>
        public void Execute(object parameter)
        {
            lock (_lock)
            {
                List<ICommand> commands = _commands.Where(command => command.CanExecute(parameter)).ToList();
                
                foreach (var command in commands)
                {
                    command.Execute(parameter);
                }
            }
        }

        /// <summary>
        /// Raises the CanExecuteChanged event.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CommandCanExecuteChanged(object sender, EventArgs e)
        {
            RaiseCanExecuteChanged();
        }
    }
}