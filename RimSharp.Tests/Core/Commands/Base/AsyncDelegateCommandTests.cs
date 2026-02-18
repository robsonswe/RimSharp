using System;
using System.ComponentModel;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Core.Commands.Base;
using Xunit;

namespace RimSharp.Tests.Core.Commands.Base
{
    public class AsyncDelegateCommandTests
    {
        [Fact]
        public async Task Execute_ShouldCallAsyncAction()
        {
            // Arrange
            bool executed = false;
            var command = new AsyncDelegateCommand(async () =>
            {
                await Task.Delay(10);
                executed = true;
            });

            // Act
            command.Execute(null);
            
            // Wait for completion (Execute is async void, so we need to wait manually or use an event)
            for (int i = 0; i < 100 && !executed; i++) await Task.Delay(10);

            // Assert
            executed.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_DuringExecution_ShouldReturnFalse()
        {
            // Arrange
            var tcs = new TaskCompletionSource<bool>();
            var command = new AsyncDelegateCommand(async () => await tcs.Task);

            // Act
            command.Execute(null);
            var result = command.CanExecute(null);

            // Assert
            result.Should().BeFalse();
            tcs.SetResult(true);
        }

        [Fact]
        public void ObservesProperty_ShouldRaiseCanExecuteChanged()
        {
            // Arrange
            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new AsyncDelegateCommand(async () => await Task.CompletedTask).ObservesProperty(owner, "TestProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            // Act
            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("TestProperty"));

            // Assert
            raised.Should().BeTrue();
        }
    }
}
