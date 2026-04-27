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

        [Fact]
        public void Execute_WhenCanExecuteIsFalse_ShouldNotCallAction()
        {

            bool executed = false;
            var command = new DelegateCommand(() => executed = true, () => false);

            command.Execute(null);

            executed.Should().BeFalse();
        }

        [Fact]
        public void CanExecute_WhenNoPredicate_ShouldAlwaysReturnTrue()
        {

            var command = new DelegateCommand(() => { });

            command.CanExecute(null).Should().BeTrue();
        }

        [Fact]
        public void ObservesProperty_UnrelatedPropertyChange_ShouldNotFireEvent()
        {

            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new DelegateCommand(() => { }).ObservesProperty(owner, "WatchedProperty");
            bool raised = false;
            command.CanExecuteChanged += (s, e) => raised = true;

            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("OtherProperty"));

            raised.Should().BeFalse();
        }

        [Fact]
        public void ObservesProperty_MultipleProperties_ShouldFireOnEither()
        {

            var owner = Substitute.For<INotifyPropertyChanged>();
            var command = new DelegateCommand(() => { })
                .ObservesProperty(owner, "PropA")
                .ObservesProperty(owner, "PropB");
            int raiseCount = 0;
            command.CanExecuteChanged += (s, e) => raiseCount++;

            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("PropA"));
            owner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(owner, new PropertyChangedEventArgs("PropB"));

            raiseCount.Should().Be(2);
        }
    }
}

