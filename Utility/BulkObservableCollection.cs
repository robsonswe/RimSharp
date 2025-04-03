using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace RimSharp.Utility // Or a suitable namespace
{
    /// <summary>
    /// An ObservableCollection that supports adding multiple items more efficiently
    /// by raising only a single CollectionChanged event (Reset).
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification = false;

        public BulkObservableCollection() : base() { }
        public BulkObservableCollection(IEnumerable<T> collection) : base(collection) { }
        public BulkObservableCollection(List<T> list) : base(list) { }


        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
             if (!_suppressNotification)
                base.OnPropertyChanged(e);
        }

        /// <summary>
        /// Adds the elements of the specified collection to the end of the ObservableCollection<T>.
        /// Raises a single CollectionChanged event with Reset action.
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null || !items.Any()) return;

            _suppressNotification = true;
            try
            {
                CheckReentrancy();
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            // Raise notifications (Count, Indexer, Reset event)
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Clears the collection and adds the elements of the specified collection.
        /// Raises a single CollectionChanged event with Reset action.
        /// </summary>
        /// <param name="items"></param>
        public void ReplaceAll(IEnumerable<T> items)
        {
            items ??= Enumerable.Empty<T>(); // Ensure items is not null

             _suppressNotification = true;
            try
            {
                CheckReentrancy();
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotification = false;
            }

            // Raise notifications
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // Optional: Implement RemoveRange if needed, similar pattern
        // public void RemoveRange(IEnumerable<T> items) { ... }
    }
}
