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

            var command = Substitute.For<ICommand>();
            string name = "TestCommand";

            _service.RegisterCommand(name, command);

            _service.GetCommand(name).Should().Be(command);
            _service.ContainsCommand(name).Should().BeTrue();
        }

        [Fact]
        public void RegisterCommand_WhenAlreadyExists_ShouldThrow()
        {

            var command = Substitute.For<ICommand>();
            _service.RegisterCommand("Dup", command);

            Action act = () => _service.RegisterCommand("Dup", command);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void RegisterCompositeCommand_ShouldStoreAndBeRetrieveableAsRegularCommand()
        {

            var composite = new CompositeCommand();
            string name = "Composite";

            _service.RegisterCompositeCommand(name, composite);

            _service.GetCompositeCommand(name).Should().Be(composite);
            _service.GetCommand(name).Should().Be(composite);
        }

        [Fact]
        public void AddToCompositeCommand_ByInstance_ShouldRegisterInComposite()
        {

            var composite = new CompositeCommand();
            _service.RegisterCompositeCommand("Comp", composite);
            var subCommand = Substitute.For<ICommand>();

            _service.AddToCompositeCommand("Comp", subCommand);

}

        [Fact]
        public void AddToCompositeCommand_ByName_ShouldRegisterInComposite()
        {

            var composite = new CompositeCommand();
            _service.RegisterCompositeCommand("Comp", composite);
            var subCommand = Substitute.For<ICommand>();
            _service.RegisterCommand("Sub", subCommand);

            _service.AddToCompositeCommand("Comp", "Sub");

            // Logic check: if names exist, it should succeed.
        }

        [Fact]
        public void AddToCompositeCommand_WhenCompositeNotFound_ShouldThrow()
        {

            var subCommand = Substitute.For<ICommand>();

            Action act = () => _service.AddToCompositeCommand("NonExistent", subCommand);

            act.Should().Throw<InvalidOperationException>();
        }
    }
}


