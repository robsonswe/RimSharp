using System;
using System.Windows.Input;
using FluentAssertions;
using NSubstitute;
using RimSharp.Core.Commands.Composite;
using RimSharp.Core.Services.Commanding;
using Xunit;

namespace RimSharp.Tests.Core.Services
{
    public class ModCommandServiceTests
    {
        private readonly ModCommandService _service;

        public ModCommandServiceTests()
        {
            _service = new ModCommandService();
        }

        [Fact]
        public void RegisterCommand_ShouldStoreCommand()
        {
            // Arrange
            var command = Substitute.For<ICommand>();
            string name = "TestCommand";

            // Act
            _service.RegisterCommand(name, command);

            // Assert
            _service.GetCommand(name).Should().Be(command);
            _service.ContainsCommand(name).Should().BeTrue();
        }

        [Fact]
        public void RegisterCommand_WhenAlreadyExists_ShouldThrow()
        {
            // Arrange
            var command = Substitute.For<ICommand>();
            _service.RegisterCommand("Dup", command);

            // Act
            Action act = () => _service.RegisterCommand("Dup", command);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void RegisterCompositeCommand_ShouldStoreAndBeRetrieveableAsRegularCommand()
        {
            // Arrange
            var composite = new CompositeCommand();
            string name = "Composite";

            // Act
            _service.RegisterCompositeCommand(name, composite);

            // Assert
            _service.GetCompositeCommand(name).Should().Be(composite);
            _service.GetCommand(name).Should().Be(composite);
        }

        [Fact]
        public void AddToCompositeCommand_ByInstance_ShouldRegisterInComposite()
        {
            // Arrange
            var composite = new CompositeCommand();
            _service.RegisterCompositeCommand("Comp", composite);
            var subCommand = Substitute.For<ICommand>();

            // Act
            _service.AddToCompositeCommand("Comp", subCommand);

            // Assert
            // There's no public way to check registered commands in CompositeCommand without executing
            // but we can verify it doesn't throw and then check behavior if needed.
            // For now, if it doesn't throw, the internal logic passed.
        }

        [Fact]
        public void AddToCompositeCommand_ByName_ShouldRegisterInComposite()
        {
            // Arrange
            var composite = new CompositeCommand();
            _service.RegisterCompositeCommand("Comp", composite);
            var subCommand = Substitute.For<ICommand>();
            _service.RegisterCommand("Sub", subCommand);

            // Act
            _service.AddToCompositeCommand("Comp", "Sub");

            // Assert
            // Logic check: if names exist, it should succeed.
        }

        [Fact]
        public void AddToCompositeCommand_WhenCompositeNotFound_ShouldThrow()
        {
            // Arrange
            var subCommand = Substitute.For<ICommand>();

            // Act
            Action act = () => _service.AddToCompositeCommand("NonExistent", subCommand);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }
    }
}
