#nullable enable
using RimSharp.AppDir.AppFiles; // For ViewModelBase

namespace RimSharp.Features.WorkshopDownloader.Dialogs.Collection
{
    public class CollectionItemViewModel : ViewModelBase
    {
        private bool _isSelected;
        public string SteamId { get; }
        public string Name { get; }
        public string Author { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public CollectionItemViewModel(string steamId, string name, string author, bool isSelected = true)
        {
            SteamId = steamId;
            Name = name ?? "Unknown Name";
            Author = author ?? "Unknown Author";
            _isSelected = isSelected;
        }
    }
}
