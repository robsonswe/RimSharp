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

            bool executed = false;
            var command = new AsyncDelegateCommand(async () =>
            {
                await Task.Delay(10);
                executed = true;
            });

            command.Execute(null);
            for (int i = 0; i < 100 && !executed; i++) await Task.Delay(10);

            executed.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_DuringExecution_ShouldReturnFalse()
        {

            var tcs = new TaskCompletionSource<bool>();
            var command = new AsyncDelegateCommand(async () => await tcs.Task);

            command.Execute(null);
            var result = command.CanExecute(null);

            result.Should().BeFalse();
            tcs.SetResult(true);
        }

        [Fact]
        public void ObservesProperty_ShouldRaiseCanExecuteChanged()
        {

            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new AsyncDelegateCommand(async () => await Task.CompletedTask).ObservesProperty(owner, "TestProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("TestProperty"));

            raised.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_WhenNoPredicate_ShouldReturnTrue()
        {

            var command = new AsyncDelegateCommand(async () => await Task.CompletedTask);

            command.CanExecute(null).Should().BeTrue();
        }

        [Fact]
        public void Execute_WhenCanExecuteIsFalse_ShouldNotInvokeAction()
        {

            bool executed = false;
            var command = new AsyncDelegateCommand(async () =>
            {
                await Task.CompletedTask;
                executed = true;
            }, () => false);

            command.Execute(null);

            executed.Should().BeFalse();
        }

        [Fact]
        public async Task Execute_WhenComplete_ShouldRaiseCanExecuteChangedAgain()
        {

            var tcs = new TaskCompletionSource<bool>();
            int changeCount = 0;
            var command = new AsyncDelegateCommand(async () => await tcs.Task);
            command.CanExecuteChanged += (s, e) => changeCount++;

            command.Execute(null); // starts execution — fires one CanExecuteChanged (false)
            tcs.SetResult(true);
            await Task.Delay(50); // let the async completion propagate

            // At least two changes: false when starting, true when done
            changeCount.Should().BeGreaterThanOrEqualTo(2);
        }
    }
}


