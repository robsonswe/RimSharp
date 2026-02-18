using System;
using System.ComponentModel;
using FluentAssertions;
using NSubstitute;
using RimSharp.Core.Commands.Base;
using Xunit;

namespace RimSharp.Tests.Core.Commands.Base
{
    public class DelegateCommandTests
    {
        [Fact]
        public void Execute_ShouldCallAction()
        {
            // Arrange
            bool executed = false;
            var command = new DelegateCommand(() => executed = true);

            // Act
            command.Execute(null);

            // Assert
            executed.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_ShouldReturnPredicateValue()
        {
            // Arrange
            var command = new DelegateCommand(() => { }, () => false);

            // Act
            var result = command.CanExecute(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ObservesProperty_ShouldRaiseCanExecuteChanged()
        {
            // Arrange
            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new DelegateCommand(() => { }).ObservesProperty(owner, "TestProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            // Act
            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("TestProperty"));

            // Assert
            raised.Should().BeTrue();
        }

        [Fact]
        public void Dispose_ShouldUnsubscribeFromOwner()
        {
            // Arrange
            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new DelegateCommand(() => { }).ObservesProperty(owner, "TestProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            // Act
            command.Dispose();
            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("TestProperty"));

            // Assert
            raised.Should().BeFalse();
        }
    }
}
