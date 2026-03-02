using FluentAssertions;
using RimSharp.Core.Commands.Base;
using Xunit;

namespace RimSharp.Tests.Core.Commands.Base
{
    public class RelayCommandTests
    {
        [Fact]
        public void Execute_ShouldCallAction()
        {

            bool executed = false;
            var command = new RelayCommand(() => executed = true);

            command.Execute(null);

            executed.Should().BeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CanExecute_ShouldReturnExpectedValue(bool expected)
        {

            var command = new RelayCommand(() => { }, () => expected);

            var result = command.CanExecute(null);

            result.Should().Be(expected);
        }

        [Fact]
        public void CanExecute_WhenNoPredicate_ShouldReturnTrue()
        {

            var command = new RelayCommand(() => { });

            var result = command.CanExecute(null);

            result.Should().BeTrue();
        }

        [Fact]
        public void Execute_WhenCanExecuteIsFalse_ShouldNotCallAction()
        {

            bool executed = false;
            var command = new RelayCommand(() => executed = true, () => false);

            command.Execute(null);

            executed.Should().BeFalse();
        }
    }
}

