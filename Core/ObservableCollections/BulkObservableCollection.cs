using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace RimSharp.Core.ObservableCollections
{
    /// <summary>

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

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>

        /// Raises a single CollectionChanged event with Reset action.
        /// </summary>
        /// <param name="items"></param>
        public void ReplaceAll(IEnumerable<T> items)
        {
            items ??= Enumerable.Empty<T>();

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

OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // Optional: Implement RemoveRange if needed, similar pattern
        // public void RemoveRange(IEnumerable<T> items) { ... }
    }
}


