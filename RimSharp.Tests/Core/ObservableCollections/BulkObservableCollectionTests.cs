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

            var collection = new BulkObservableCollection<int>();
            var itemsToAdd = new List<int> { 1, 2, 3 };

            collection.AddRange(itemsToAdd);

            collection.Should().HaveCount(3);
            collection.Should().ContainInOrder(itemsToAdd);
        }

        [Fact]
        public void AddRange_ShouldRaiseSingleCollectionChangedEvent()
        {

            var collection = new BulkObservableCollection<int>();
            var itemsToAdd = new List<int> { 1, 2, 3 };
            int eventCount = 0;
            collection.CollectionChanged += (s, e) =>
            {
                eventCount++;
                e.Action.Should().Be(NotifyCollectionChangedAction.Reset);
            };

            collection.AddRange(itemsToAdd);

            eventCount.Should().Be(1);
        }

        [Fact]
        public void ReplaceAll_ShouldClearAndAddItems()
        {

            var collection = new BulkObservableCollection<int> { 10, 20 };
            var newItems = new List<int> { 1, 2, 3 };

            collection.ReplaceAll(newItems);

            collection.Should().HaveCount(3);
            collection.Should().NotContain(10);
            collection.Should().ContainInOrder(newItems);
        }

        [Fact]
        public void AddRange_ShouldRaisePropertyChangedForCountAndIndexer()
        {

            var collection = new TestableBulkObservableCollection<int>();
            var raisedProperties = new List<string>();
            collection.PropertyChanged += (s, e) => raisedProperties.Add(e.PropertyName!);

            collection.AddRange(new[] { 1 });

            raisedProperties.Should().Contain("Count");
            raisedProperties.Should().Contain("Item[]");
        }

        [Fact]
        public void AddRange_WhenItemsIsEmpty_ShouldNotModifyCollectionOrRaiseEvents()
        {

            var collection = new BulkObservableCollection<int> { 1, 2 };
            int eventCount = 0;
            collection.CollectionChanged += (s, e) => eventCount++;

            collection.AddRange(new List<int>());

            collection.Should().HaveCount(2);
            eventCount.Should().Be(0);
        }

        [Fact]
        public void AddRange_WhenItemsIsNull_ShouldNotModifyCollectionOrRaiseEvents()
        {

            var collection = new BulkObservableCollection<int> { 1 };
            int eventCount = 0;
            collection.CollectionChanged += (s, e) => eventCount++;

            collection.AddRange(null!);

            collection.Should().HaveCount(1);
            eventCount.Should().Be(0);
        }

        [Fact]
        public void ReplaceAll_WhenItemsIsEmpty_ShouldClearCollection()
        {

            var collection = new BulkObservableCollection<int> { 10, 20, 30 };

            collection.ReplaceAll(new List<int>());

            collection.Should().BeEmpty();
        }

        [Fact]
        public void ReplaceAll_ShouldRaiseSingleCollectionChangedEvent()
        {

            var collection = new BulkObservableCollection<int> { 1, 2 };
            int eventCount = 0;
            collection.CollectionChanged += (s, e) =>
            {
                eventCount++;
                e.Action.Should().Be(System.Collections.Specialized.NotifyCollectionChangedAction.Reset);
            };

            collection.ReplaceAll(new List<int> { 3, 4, 5 });

            eventCount.Should().Be(1);
        }

        [Fact]
        public void ReplaceAll_WhenItemsIsNull_ShouldClearCollection()
        {

            var collection = new BulkObservableCollection<int> { 1, 2 };

            collection.ReplaceAll(null!);

            collection.Should().BeEmpty();
        }
    }
}

