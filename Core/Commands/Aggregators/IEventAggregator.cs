using System;

namespace RimSharp.Core.Commands.Aggregators
{
    /// <summary>

    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        /// Publishes an event to all subscribers.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to publish.</typeparam>
        /// <param name="eventToPublish">The event to publish.</param>
        void Publish<TEvent>(TEvent eventToPublish);

        /// <summary>
        /// Subscribes to an event.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>

        /// <returns>A token that can be used to unsubscribe.</returns>
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> action);

        /// <summary>
        /// Unsubscribes from an event.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to unsubscribe from.</typeparam>
        /// <param name="token">The subscription token.</param>
        void Unsubscribe<TEvent>(SubscriptionToken token);
    }

    /// <summary>
    /// Token that represents a subscription to an event.
    /// </summary>
    public class SubscriptionToken
    {
        private readonly Guid _token;
        private readonly Type _eventType;

        /// <summary>
        /// Gets the token's unique identifier.
        /// </summary>
        public Guid Token => _token;

        /// <summary>

        /// </summary>
        public Type EventType => _eventType;

        /// <summary>

        /// </summary>

        public SubscriptionToken(Type eventType)
        {
            _token = Guid.NewGuid();
            _eventType = eventType;
        }
    }
}
