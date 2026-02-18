using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using FluentAssertions;
using RimSharp.Core.ObservableCollections;
using Xunit;

namespace RimSharp.Tests.Core.ObservableCollections
{
    public class BulkObservableCollectionTests
    {
        // Expose protected event for testing
        private class TestableBulkObservableCollection<T> : BulkObservableCollection<T>
        {
            public new event PropertyChangedEventHandler PropertyChanged
            {
                add => base.PropertyChanged += value;
                remove => base.PropertyChanged -= value;
            }
        }

        [Fact]
        public void AddRange_ShouldAddAllItems()
        {
            // Arrange
            var collection = new BulkObservableCollection<int>();
            var itemsToAdd = new List<int> { 1, 2, 3 };

            // Act
            collection.AddRange(itemsToAdd);

            // Assert
            collection.Should().HaveCount(3);
            collection.Should().ContainInOrder(itemsToAdd);
        }

        [Fact]
        public void AddRange_ShouldRaiseSingleCollectionChangedEvent()
        {
            // Arrange
            var collection = new BulkObservableCollection<int>();
            var itemsToAdd = new List<int> { 1, 2, 3 };
            int eventCount = 0;
            collection.CollectionChanged += (s, e) =>
            {
                eventCount++;
                e.Action.Should().Be(NotifyCollectionChangedAction.Reset);
            };

            // Act
            collection.AddRange(itemsToAdd);

            // Assert
            eventCount.Should().Be(1);
        }

        [Fact]
        public void ReplaceAll_ShouldClearAndAddItems()
        {
            // Arrange
            var collection = new BulkObservableCollection<int> { 10, 20 };
            var newItems = new List<int> { 1, 2, 3 };

            // Act
            collection.ReplaceAll(newItems);

            // Assert
            collection.Should().HaveCount(3);
            collection.Should().NotContain(10);
            collection.Should().ContainInOrder(newItems);
        }

        [Fact]
        public void AddRange_ShouldRaisePropertyChangedForCountAndIndexer()
        {
             // Arrange
            var collection = new TestableBulkObservableCollection<int>();
            var raisedProperties = new List<string>();
            collection.PropertyChanged += (s, e) => raisedProperties.Add(e.PropertyName!);

            // Act
            collection.AddRange(new[] { 1 });

            // Assert
            raisedProperties.Should().Contain("Count");
            raisedProperties.Should().Contain("Item[]");
        }
    }
}
