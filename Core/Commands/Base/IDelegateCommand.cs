using System;
using System.Windows.Input;

namespace RimSharp.Core.Commands.Base
{
    /// <summary>
    /// Interface for delegate commands that extends ICommand with additional functionality.
    /// </summary>
    public interface IDelegateCommand : ICommand
    {
        /// <summary>
        /// Raises the CanExecuteChanged event to force a re-evaluation of command execution ability.
        /// </summary>
        void RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Interface for generic delegate commands that extends ICommand with additional functionality.
    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public interface IDelegateCommand<T> : ICommand
    {
        /// <summary>
        /// Raises the CanExecuteChanged event to force a re-evaluation of command execution ability.
        /// </summary>
        void RaiseCanExecuteChanged();
    }
}