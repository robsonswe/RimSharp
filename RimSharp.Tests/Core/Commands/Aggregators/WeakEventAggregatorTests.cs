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

            string? receivedMessage = null;
            Action<TestEvent> action = e => receivedMessage = e.Message;
            _aggregator.Subscribe(action);

            _aggregator.Publish(new TestEvent { Message = "Hello" });

            receivedMessage.Should().Be("Hello");
        }

        [Fact]
        public void Unsubscribe_ShouldStopNotifications()
        {

            int callCount = 0;
            Action<TestEvent> action = e => callCount++;
            var token = _aggregator.Subscribe(action);

            _aggregator.Publish(new TestEvent());
            _aggregator.Unsubscribe<TestEvent>(token);
            _aggregator.Publish(new TestEvent());

            callCount.Should().Be(1);
        }

        [Fact]
        public void Unsubscribe_WithWrongType_ShouldThrow()
        {

            Action<TestEvent> action = e => { };
            var token = _aggregator.Subscribe(action);

            var act = () => _aggregator.Unsubscribe<string>(token);

            act.Should().Throw<ArgumentException>().WithMessage("*string*");
        }

        [Fact]
        public void Purge_ShouldRemoveDeadReferences()
        {

            SetupDeadSubscription();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            _aggregator.Purge();

}

        private void SetupDeadSubscription()
        {
            Action<TestEvent> action = e => { };
            _aggregator.Subscribe(action);
            // 'action' goes out of scope here
        }
    }
}


