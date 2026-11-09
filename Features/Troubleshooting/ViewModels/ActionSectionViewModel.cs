#nullable enable
using System.Collections.ObjectModel;
using ReactiveUI;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.Troubleshooting.ViewModels
{
    /// <summary>
    /// One section of the grouped action catalog (Game Files Recovery / Mod Configuration / Steam Utilities).
    /// </summary>
    public sealed class ActionSectionViewModel : ReactiveObject
    {
        public string Title { get; }
        public string? Description { get; }
        public bool IsSteamSection { get; }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }

        public ObservableCollection<ActionRowViewModel> Actions { get; } = new();

        public ActionSectionViewModel(string title, string? description = null, bool isSteamSection = false)
        {
            Title = title;
            Description = description;
            IsSteamSection = isSteamSection;
            _isVisible = true;
        }
    }
}
