using System;
using System.Windows.Input;

namespace RimSharp.Core.Commands.Base
{
    /// <summary>

    /// </summary>
    public interface IDelegateCommand : ICommand
    {
        /// <summary>

        /// </summary>
        void RaiseCanExecuteChanged();
    }

    /// <summary>

    /// </summary>
    /// <typeparam name="T">The type of parameter the command accepts.</typeparam>
    public interface IDelegateCommand<T> : ICommand
    {
        /// <summary>

        /// </summary>
        void RaiseCanExecuteChanged();
    }
}
