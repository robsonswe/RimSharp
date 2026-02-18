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
            // Arrange
            bool executed = false;
            var command = new RelayCommand(() => executed = true);

            // Act
            command.Execute(null);

            // Assert
            executed.Should().BeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CanExecute_ShouldReturnExpectedValue(bool expected)
        {
            // Arrange
            var command = new RelayCommand(() => { }, () => expected);

            // Act
            var result = command.CanExecute(null);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void CanExecute_WhenNoPredicate_ShouldReturnTrue()
        {
            // Arrange
            var command = new RelayCommand(() => { });

            // Act
            var result = command.CanExecute(null);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void Execute_WhenCanExecuteIsFalse_ShouldNotCallAction()
        {
            // Arrange
            bool executed = false;
            var command = new RelayCommand(() => executed = true, () => false);

            // Act
            command.Execute(null);

            // Assert
            executed.Should().BeFalse();
        }
    }
}
