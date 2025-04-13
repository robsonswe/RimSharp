using System;
using System.Collections.Generic;
using System.Linq;

namespace RimSharp.Core.Commands.Aggregators
{
    /// <summary>
    /// An implementation of IEventAggregator that uses weak references to prevent memory leaks.
    /// </summary>
    public class WeakEventAggregator : IEventAggregator
    {
        private readonly Dictionary<Type, Dictionary<Guid, WeakReference>> _subscribers = new Dictionary<Type, Dictionary<Guid, WeakReference>>();
        private readonly object _lock = new object();

        /// <summary>
        /// Publishes an event to all subscribers.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to publish.</typeparam>
        /// <param name="eventToPublish">The event to publish.</param>
        public void Publish<TEvent>(TEvent eventToPublish)
        {
            if (eventToPublish == null) throw new ArgumentNullException(nameof(eventToPublish));

            Dictionary<Guid, WeakReference> eventSubscribers;
            List<Guid> deadReferences = new List<Guid>();

            lock (_lock)
            {
                if (!_subscribers.TryGetValue(typeof(TEvent), out eventSubscribers))
                {
                    return;
                }

                // Create a snapshot to avoid modification exceptions during iteration
                eventSubscribers = new Dictionary<Guid, WeakReference>(eventSubscribers);
            }

            // Invoke subscribers and collect dead references
            foreach (var subscription in eventSubscribers)
            {
                if (subscription.Value.Target is Action<TEvent> action)
                {
                    action(eventToPublish);
                }
                else
                {
                    deadReferences.Add(subscription.Key);
                }
            }

            // Clean up dead references
            if (deadReferences.Count > 0)
            {
                lock (_lock)
                {
                    if (_subscribers.TryGetValue(typeof(TEvent), out var currentSubscribers))
                    {
                        foreach (var key in deadReferences)
                        {
                            currentSubscribers.Remove(key);
                        }

                        // If no subscribers remain, remove the event type
                        if (currentSubscribers.Count == 0)
                        {
                            _subscribers.Remove(typeof(TEvent));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Subscribes to an event.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
        /// <param name="action">The action to execute when the event is published.</param>
        /// <returns>A token that can be used to unsubscribe.</returns>
        public SubscriptionToken Subscribe<TEvent>(Action<TEvent> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var token = new SubscriptionToken(typeof(TEvent));

            lock (_lock)
            {
                if (!_subscribers.TryGetValue(typeof(TEvent), out var eventSubscribers))
                {
                    eventSubscribers = new Dictionary<Guid, WeakReference>();
                    _subscribers[typeof(TEvent)] = eventSubscribers;
                }

                eventSubscribers[token.Token] = new WeakReference(action);
            }

            return token;
        }

        /// <summary>
        /// Unsubscribes from an event.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to unsubscribe from.</typeparam>
        /// <param name="token">The subscription token.</param>
        public void Unsubscribe<TEvent>(SubscriptionToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (token.EventType != typeof(TEvent))
            {
                throw new ArgumentException($"Token is for event type {token.EventType.Name} but was used to unsubscribe from {typeof(TEvent).Name}");
            }

            lock (_lock)
            {
                if (_subscribers.TryGetValue(typeof(TEvent), out var eventSubscribers))
                {
                    if (eventSubscribers.Remove(token.Token) && eventSubscribers.Count == 0)
                    {
                        _subscribers.Remove(typeof(TEvent));
                    }
                }
            }
        }

        /// <summary>
        /// Removes all dead references from the subscribers dictionary.
        /// </summary>
        public void Purge()
        {
            lock (_lock)
            {
                List<Type> emptyTypes = new List<Type>();

                foreach (var eventType in _subscribers.Keys.ToList())
                {
                    var eventSubscribers = _subscribers[eventType];
                    List<Guid> deadReferences = new List<Guid>();

                    foreach (var subscription in eventSubscribers)
                    {
                        if (!subscription.Value.IsAlive)
                        {
                            deadReferences.Add(subscription.Key);
                        }
                    }

                    foreach (var key in deadReferences)
                    {
                        eventSubscribers.Remove(key);
                    }

                    if (eventSubscribers.Count == 0)
                    {
                        emptyTypes.Add(eventType);
                    }
                }

                foreach (var type in emptyTypes)
                {
                    _subscribers.Remove(type);
                }
            }
        }
    }
}