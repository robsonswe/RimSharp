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

            var composite = new CompositeCommand();
            var command = Substitute.For<ICommand>();

            composite.RegisterCommand(command);

            composite.RegisteredCommands.Should().Contain(command);
        }

        [Fact]
        public void UnregisterCommand_ShouldRemoveFromList()
        {

            var composite = new CompositeCommand();
            var command = Substitute.For<ICommand>();
            composite.RegisterCommand(command);

            composite.UnregisterCommand(command);

            composite.RegisteredCommands.Should().NotContain(command);
        }

        [Fact]
        public void CanExecute_WhenAllCommandsCanExecute_ShouldReturnTrue()
        {

            var composite = new CompositeCommand();
            var cmd1 = Substitute.For<ICommand>();
            var cmd2 = Substitute.For<ICommand>();
            cmd1.CanExecute(null).Returns(true);
            cmd2.CanExecute(null).Returns(true);
            composite.RegisterCommand(cmd1);
            composite.RegisterCommand(cmd2);

            var result = composite.CanExecute(null);

            result.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_WhenAnyCommandCannotExecute_ShouldReturnFalse()
        {

            var composite = new CompositeCommand();
            var cmd1 = Substitute.For<ICommand>();
            var cmd2 = Substitute.For<ICommand>();
            cmd1.CanExecute(null).Returns(true);
            cmd2.CanExecute(null).Returns(false);
            composite.RegisterCommand(cmd1);
            composite.RegisterCommand(cmd2);

            var result = composite.CanExecute(null);

            result.Should().BeFalse();
        }

        [Fact]
        public void CanExecute_WhenNoCommands_ShouldReturnFalse()
        {

            var composite = new CompositeCommand();

            var result = composite.CanExecute(null);

            result.Should().BeFalse();
        }

        [Fact]
        public void Execute_ShouldExecuteAllCanExecuteCommands()
        {

            var composite = new CompositeCommand();
            var cmd1 = Substitute.For<ICommand>();
            var cmd2 = Substitute.For<ICommand>();
            cmd1.CanExecute(null).Returns(true);
            cmd2.CanExecute(null).Returns(false);
            composite.RegisterCommand(cmd1);
            composite.RegisterCommand(cmd2);

            composite.Execute(null);

            cmd1.Received(1).Execute(null);
            cmd2.DidNotReceive().Execute(null);
        }

        [Fact]
        public void RaiseCanExecuteChanged_ShouldRaiseEvent()
        {

            var composite = new CompositeCommand();
            bool raised = false;
            composite.CanExecuteChanged += (s, e) => raised = true;

            composite.RaiseCanExecuteChanged();

            raised.Should().BeTrue();
        }

        [Fact]
        public void CommandCanExecuteChanged_ShouldRaiseCompositeEvent()
        {

            var composite = new CompositeCommand(true);
            var command = Substitute.For<ICommand>();
            bool raised = false;
            composite.CanExecuteChanged += (s, e) => raised = true;
            composite.RegisterCommand(command);

            command.CanExecuteChanged += Raise.Event();

            raised.Should().BeTrue();
        }
    }
}

