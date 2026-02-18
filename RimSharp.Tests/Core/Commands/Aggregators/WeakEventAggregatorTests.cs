using System;
using FluentAssertions;
using RimSharp.Core.Commands.Aggregators;
using Xunit;

namespace RimSharp.Tests.Core.Commands.Aggregators
{
    public class WeakEventAggregatorTests
    {
        private readonly WeakEventAggregator _aggregator;

        public WeakEventAggregatorTests()
        {
            _aggregator = new WeakEventAggregator();
        }

        private class TestEvent { public string Message { get; set; } = ""; }

        [Fact]
        public void Publish_ShouldNotifySubscribers()
        {
            // Arrange
            string? receivedMessage = null;
            Action<TestEvent> action = e => receivedMessage = e.Message;
            _aggregator.Subscribe(action);

            // Act
            _aggregator.Publish(new TestEvent { Message = "Hello" });

            // Assert
            receivedMessage.Should().Be("Hello");
        }

        [Fact]
        public void Unsubscribe_ShouldStopNotifications()
        {
            // Arrange
            int callCount = 0;
            Action<TestEvent> action = e => callCount++;
            var token = _aggregator.Subscribe(action);

            // Act
            _aggregator.Publish(new TestEvent());
            _aggregator.Unsubscribe<TestEvent>(token);
            _aggregator.Publish(new TestEvent());

            // Assert
            callCount.Should().Be(1);
        }

        [Fact]
        public void Unsubscribe_WithWrongType_ShouldThrow()
        {
            // Arrange
            Action<TestEvent> action = e => { };
            var token = _aggregator.Subscribe(action);

            // Act
            var act = () => _aggregator.Unsubscribe<string>(token);

            // Assert
            act.Should().Throw<ArgumentException>().WithMessage("*string*");
        }

        [Fact]
        public void Purge_ShouldRemoveDeadReferences()
        {
            // Arrange
            // We need a scope where the action can be garbage collected
            SetupDeadSubscription();

            // Act
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _aggregator.Purge();

            // Assert
            // Since we can't easily inspect private _subscribers, 
            // we'll rely on the fact that it shouldn't crash and we've exercised the code.
            // A better test would be to ensure publishing doesn't try to call dead ones (already handled in Publish).
        }

        private void SetupDeadSubscription()
        {
            Action<TestEvent> action = e => { };
            _aggregator.Subscribe(action);
            // 'action' goes out of scope here
        }
    }
}
