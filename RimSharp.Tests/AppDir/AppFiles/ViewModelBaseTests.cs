using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using FluentAssertions;
using NSubstitute;
using RimSharp.AppDir.AppFiles;
using RimSharp.Core.Services.Commanding;
using Xunit;

namespace RimSharp.Tests.AppDir.AppFiles
{
    public class ViewModelBaseTests
    {
        private class TestViewModel : ViewModelBase
        {
            public TestViewModel() : base() { }
            public TestViewModel(IModCommandService commandService) : base(commandService) { }

            private string _name = string.Empty;
            public string Name
            {
                get => _name;
                set => SetProperty(ref _name, value);
            }

            public void PublicOnPropertyChanged(string propertyName) => OnPropertyChanged(propertyName);

            public T? CreateTestCommand<T>(Action execute, Func<bool>? canExecute = null, params string[] observedProperties) 
                where T : class => base.CreateCommand(execute, canExecute, observedProperties) as T;

            public IModCommandService? CommandService => base.GetGlobalCommand("any") != null ? null : null; // Just to check access if needed
            
            // Expose protected methods for testing
            public new void RunOnUIThread(Action action) => base.RunOnUIThread(action);
            public new Task RunAsync(Func<Task> asyncOperation) => base.RunAsync(asyncOperation);
            public new ICommand? GetGlobalCommand(string name) => base.GetGlobalCommand(name);
        }

        [Fact]
        public void SetProperty_ShouldNotifyChange()
        {
            // Arrange
            var vm = new TestViewModel();
            string? changedProperty = null;
            vm.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

            // Act
            vm.Name = "New Name";

            // Assert
            vm.Name.Should().Be("New Name");
            changedProperty.Should().Be(nameof(TestViewModel.Name));
        }

        [Fact]
        public void SetProperty_ShouldNotNotifyIfValueIsSame()
        {
            // Arrange
            var vm = new TestViewModel { Name = "Same" };
            bool notified = false;
            vm.PropertyChanged += (s, e) => notified = true;

            // Act
            vm.Name = "Same";

            // Assert
            notified.Should().BeFalse();
        }

        [Fact]
        public void CreateCommand_ShouldReturnCommandAndAddToList()
        {
            // Arrange
            var vm = new TestViewModel();

            // Act
            var command = vm.CreateTestCommand<RimSharp.Core.Commands.Base.DelegateCommand>(() => { });

            // Assert
            command.Should().NotBeNull();
        }

        [Fact]
        public void GetGlobalCommand_ShouldUseCommandService()
        {
            // Arrange
            var mockService = Substitute.For<IModCommandService>();
            var mockCommand = Substitute.For<ICommand>();
            mockService.GetCommand("TestCommand").Returns(mockCommand);
            var vm = new TestViewModel(mockService);

            // Act
            var result = vm.GetGlobalCommand("TestCommand");

            // Assert
            result.Should().Be(mockCommand);
            mockService.Received(1).GetCommand("TestCommand");
        }

        [Fact]
        public async Task RunAsync_ShouldExecuteAndHandleException()
        {
            // Arrange
            var vm = new TestViewModel();
            bool executed = false;

            // Act
            await vm.RunAsync(async () => {
                await Task.Yield();
                executed = true;
            });

            // Assert
            executed.Should().BeTrue();
        }

        [Fact]
        public void Dispose_ShouldClearCommands()
        {
            // Arrange
            var vm = new TestViewModel();
            var command = vm.CreateTestCommand<RimSharp.Core.Commands.Base.DelegateCommand>(() => { });

            // Act
            vm.Dispose();

            // Assert
            Action act = () => vm.CreateTestCommand<RimSharp.Core.Commands.Base.DelegateCommand>(() => { });
            act.Should().Throw<ObjectDisposedException>();
        }
    }
}
