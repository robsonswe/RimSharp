#nullable enable
using ReactiveUI;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.Troubleshooting.ViewModels
{
    /// <summary>
    /// One row in the grouped "All recovery actions" catalog.
    /// Visibility is gating-driven (hidden, never grayed).
    /// </summary>
    public sealed class ActionRowViewModel : ReactiveObject
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Icon { get; }
        public string Section { get; }
        public bool IsSteamRequired { get; }
        public bool IsDestructive { get; }
        public bool RequiresConfirmation { get; }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set => this.RaiseAndSetIfChanged(ref _isRunning, value);
        }

        private string _buttonLabel;
        public string ButtonLabel
        {
            get => _buttonLabel;
            set => this.RaiseAndSetIfChanged(ref _buttonLabel, value);
        }

        /// <summary>Severity chip text ("Error", "Warning", "Info") when a scan result maps to this action.</summary>
        private string? _severityChip;
        public string? SeverityChip
        {
            get => _severityChip;
            set => this.RaiseAndSetIfChanged(ref _severityChip, value);
        }

        public ActionRowViewModel(
            string id,
            string title,
            string description,
            string icon,
            string section,
            bool isSteamRequired,
            bool isDestructive,
            bool requiresConfirmation,
            bool isVisible)
        {
            Id = id;
            Title = title;
            Description = description;
            Icon = icon;
            Section = section;
            IsSteamRequired = isSteamRequired;
            IsDestructive = isDestructive;
            RequiresConfirmation = requiresConfirmation;
            _isVisible = isVisible;
            _buttonLabel = title;
        }

        public void SetRunning(bool running)
        {
            IsRunning = running;
            ButtonLabel = running ? "Working…" : Title;
        }
    }
}
