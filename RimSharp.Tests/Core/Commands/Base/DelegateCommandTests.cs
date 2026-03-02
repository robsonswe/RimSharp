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

            bool executed = false;
            var command = new DelegateCommand(() => executed = true);

            command.Execute(null);

            executed.Should().BeTrue();
        }

        [Fact]
        public void CanExecute_ShouldReturnPredicateValue()
        {

            var command = new DelegateCommand(() => { }, () => false);

            var result = command.CanExecute(null);

            result.Should().BeFalse();
        }

        [Fact]
        public void ObservesProperty_ShouldRaiseCanExecuteChanged()
        {

            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new DelegateCommand(() => { }).ObservesProperty(owner, "TestProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("TestProperty"));

            raised.Should().BeTrue();
        }

        [Fact]
        public void Dispose_ShouldUnsubscribeFromOwner()
        {

            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new DelegateCommand(() => { }).ObservesProperty(owner, "TestProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            command.Dispose();
            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("TestProperty"));

            raised.Should().BeFalse();
        }
    }
}

