using System;
using System.Windows.Input;
using FluentAssertions;
using NSubstitute;
using RimSharp.Core.Commands.Composite;
using Xunit;

namespace RimSharp.Tests.Core.Commands.Composite
{
    public class CompositeCommandTests
    {
        [Fact]
        public void RegisterCommand_ShouldAddToList()
        {
            // Arrange
            var composite = new CompositeCommand();
            var command = Substitute.For<ICommand>();

            // Act
            composite.RegisterCommand(command);

            // Assert
            composite.RegisteredCommands.Should().Contain(command);
        }

        [Fact]
        public void UnregisterCommand_ShouldRemoveFromList()
        {
            // Arrange
            var composite = new CompositeCommand();
            var command = Substitute.For<ICommand>();
            composite.RegisterCommand(command);

            // Act
            composite.UnregisterCommand(command);

            // Assert
            composite.RegisteredCommands.Should().NotContain(command);
        }

        [Fact]
        public void CanExecute_WhenAllCommandsCanExecute_ShouldReturnTrue()
        {
            // Arrange
            var composite = new CompositeCommand();
            var cmd1 = Substitute.For<ICommand>();
            var cmd2 = Substitute.For<ICommand>();
            cmd1.CanExecute(null).Returns(true);
            cmd2.CanExecute(null).Returns(true);
            composite.RegisterCommand(cmd1);
            composite.RegisterCommand(cmd2);

            // Act
            var result = composite.CanExecute(null);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_WhenAnyCommandCannotExecute_ShouldReturnFalse()
        {
            // Arrange
            var composite = new CompositeCommand();
            var cmd1 = Substitute.For<ICommand>();
            var cmd2 = Substitute.For<ICommand>();
            cmd1.CanExecute(null).Returns(true);
            cmd2.CanExecute(null).Returns(false);
            composite.RegisterCommand(cmd1);
            composite.RegisterCommand(cmd2);

            // Act
            var result = composite.CanExecute(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void CanExecute_WhenNoCommands_ShouldReturnFalse()
        {
            // Arrange
            var composite = new CompositeCommand();

            // Act
            var result = composite.CanExecute(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Execute_ShouldExecuteAllCanExecuteCommands()
        {
            // Arrange
            var composite = new CompositeCommand();
            var cmd1 = Substitute.For<ICommand>();
            var cmd2 = Substitute.For<ICommand>();
            cmd1.CanExecute(null).Returns(true);
            cmd2.CanExecute(null).Returns(false);
            composite.RegisterCommand(cmd1);
            composite.RegisterCommand(cmd2);

            // Act
            composite.Execute(null);

            // Assert
            cmd1.Received(1).Execute(null);
            cmd2.DidNotReceive().Execute(null);
        }

        [Fact]
        public void RaiseCanExecuteChanged_ShouldRaiseEvent()
        {
            // Arrange
            var composite = new CompositeCommand();
            bool raised = false;
            composite.CanExecuteChanged += (s, e) => raised = true;

            // Act
            composite.RaiseCanExecuteChanged();

            // Assert
            raised.Should().BeTrue();
        }

        [Fact]
        public void CommandCanExecuteChanged_ShouldRaiseCompositeEvent()
        {
            // Arrange
            var composite = new CompositeCommand(true);
            var command = Substitute.For<ICommand>();
            bool raised = false;
            composite.CanExecuteChanged += (s, e) => raised = true;
            composite.RegisterCommand(command);

            // Act
            command.CanExecuteChanged += Raise.Event();

            // Assert
            raised.Should().BeTrue();
        }
    }
}
