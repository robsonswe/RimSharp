using RimSharp.MyApp.AppFiles; // For ViewModelBase

namespace RimSharp.Features.ModManager.Dialogs.Filter
{
    public class SelectableItemViewModel<T> : ViewModelBase
    {
        private bool _isSelected;
        public T Item { get; }
        public string DisplayName { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public SelectableItemViewModel(T item, string displayName, bool isSelected = false)
        {
            Item = item;
            DisplayName = displayName;
            _isSelected = isSelected;
        }
    }
}
