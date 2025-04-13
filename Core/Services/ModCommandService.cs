using System;
using System.Collections.Generic;
using System.Windows.Input;
using RimSharp.Core.Commands.Composite;

namespace RimSharp.Core.Services.Commanding
{
    /// <summary>
    /// Interface for a service that manages commands for the application.
    /// </summary>
    public interface IModCommandService
    {
        /// <summary>
        /// Registers a command with the service.
        /// </summary>
        /// <param name="commandName">The name of the command.</param>
        /// <param name="command">The command to register.</param>
        void RegisterCommand(string commandName, ICommand command);

        /// <summary>
        /// Registers a composite command with the service.
        /// </summary>
        /// <param name="commandName">The name of the command.</param>
        /// <param name="command">The composite command to register.</param>
        void RegisterCompositeCommand(string commandName, CompositeCommand command);

        /// <summary>
        /// Gets a command from the service.
        /// </summary>
        /// <param name="commandName">The name of the command to get.</param>
        /// <returns>The command, or null if not found.</returns>
        ICommand GetCommand(string commandName);

        /// <summary>
        /// Gets a composite command from the service.
        /// </summary>
        /// <param name="commandName">The name of the composite command to get.</param>
        /// <returns>The composite command, or null if not found.</returns>
        CompositeCommand GetCompositeCommand(string commandName);

        /// <summary>
        /// Adds a command to a composite command.
        /// </summary>
        /// <param name="compositeName">The name of the composite command.</param>
        /// <param name="command">The command to add.</param>
        void AddToCompositeCommand(string compositeName, ICommand command);

        /// <summary>
        /// Adds a named command to a composite command.
        /// </summary>
        /// <param name="compositeName">The name of the composite command.</param>
        /// <param name="commandName">The name of the command to add.</param>
        void AddToCompositeCommand(string compositeName, string commandName);

        /// <summary>
        /// Determines whether a command is registered with the service.
        /// </summary>
        /// <param name="commandName">The name of the command to check.</param>
        /// <returns>True if the command is registered; otherwise, false.</returns>
        bool ContainsCommand(string commandName);
    }

    /// <summary>
    /// A service that manages commands for the application.
    /// </summary>
    public class ModCommandService : IModCommandService
    {
        private readonly Dictionary<string, ICommand> _commands = new Dictionary<string, ICommand>();
        private readonly Dictionary<string, CompositeCommand> _compositeCommands = new Dictionary<string, CompositeCommand>();
        private readonly object _lock = new object();

        /// <summary>
        /// Registers a command with the service.
        /// </summary>
        /// <param name="commandName">The name of the command.</param>
        /// <param name="command">The command to register.</param>
        public void RegisterCommand(string commandName, ICommand command)
        {
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentNullException(nameof(commandName));
            if (command == null) throw new ArgumentNullException(nameof(command));

            lock (_lock)
            {
                if (_commands.ContainsKey(commandName))
                {
                    throw new InvalidOperationException($"A command with the name '{commandName}' is already registered.");
                }

                _commands[commandName] = command;
            }
        }

        /// <summary>
        /// Registers a composite command with the service.
        /// </summary>
        /// <param name="commandName">The name of the command.</param>
        /// <param name="command">The composite command to register.</param>
        public void RegisterCompositeCommand(string commandName, CompositeCommand command)
        {
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentNullException(nameof(commandName));
            if (command == null) throw new ArgumentNullException(nameof(command));

            lock (_lock)
            {
                if (_compositeCommands.ContainsKey(commandName))
                {
                    throw new InvalidOperationException($"A composite command with the name '{commandName}' is already registered.");
                }

                _compositeCommands[commandName] = command;
                
                // Also register in the regular commands collection for easier lookup
                _commands[commandName] = command;
            }
        }

        /// <summary>
        /// Gets a command from the service.
        /// </summary>
        /// <param name="commandName">The name of the command to get.</param>
        /// <returns>The command, or null if not found.</returns>
        public ICommand GetCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentNullException(nameof(commandName));

            lock (_lock)
            {
                return _commands.TryGetValue(commandName, out var command) ? command : null;
            }
        }

        /// <summary>
        /// Gets a composite command from the service.
        /// </summary>
        /// <param name="commandName">The name of the composite command to get.</param>
        /// <returns>The composite command, or null if not found.</returns>
        public CompositeCommand GetCompositeCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentNullException(nameof(commandName));

            lock (_lock)
            {
                return _compositeCommands.TryGetValue(commandName, out var command) ? command : null;
            }
        }

        /// <summary>
        /// Adds a command to a composite command.
        /// </summary>
        /// <param name="compositeName">The name of the composite command.</param>
        /// <param name="command">The command to add.</param>
        public void AddToCompositeCommand(string compositeName, ICommand command)
        {
            if (string.IsNullOrEmpty(compositeName)) throw new ArgumentNullException(nameof(compositeName));
            if (command == null) throw new ArgumentNullException(nameof(command));

            lock (_lock)
            {
                if (!_compositeCommands.TryGetValue(compositeName, out var compositeCommand))
                {
                    throw new InvalidOperationException($"No composite command with the name '{compositeName}' is registered.");
                }

                compositeCommand.RegisterCommand(command);
            }
        }

        /// <summary>
        /// Adds a named command to a composite command.
        /// </summary>
        /// <param name="compositeName">The name of the composite command.</param>
        /// <param name="commandName">The name of the command to add.</param>
        public void AddToCompositeCommand(string compositeName, string commandName)
        {
            if (string.IsNullOrEmpty(compositeName)) throw new ArgumentNullException(nameof(compositeName));
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentNullException(nameof(commandName));

            lock (_lock)
            {
                if (!_compositeCommands.TryGetValue(compositeName, out var compositeCommand))
                {
                    throw new InvalidOperationException($"No composite command with the name '{compositeName}' is registered.");
                }

                if (!_commands.TryGetValue(commandName, out var command))
                {
                    throw new InvalidOperationException($"No command with the name '{commandName}' is registered.");
                }

                compositeCommand.RegisterCommand(command);
            }
        }

        /// <summary>
        /// Determines whether a command is registered with the service.
        /// </summary>
        /// <param name="commandName">The name of the command to check.</param>
        /// <returns>True if the command is registered; otherwise, false.</returns>
        public bool ContainsCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName)) throw new ArgumentNullException(nameof(commandName));

            lock (_lock)
            {
                return _commands.ContainsKey(commandName);
            }
        }
    }
}