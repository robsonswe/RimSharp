#nullable enable
using ReactiveUI;
using RimSharp.AppDir.AppFiles;

namespace RimSharp.Features.Troubleshooting.ViewModels
{
    /// <summary>
    /// Severity of an inline toast shown at the bottom of the Troubleshooting dialog.
    /// </summary>
    public enum ToastType
    {
        Success,
        Error,
        Warning,
        Info
    }

    /// <summary>
    /// One inline toast: title (action name), message (outcome), and severity styling.
    /// Dismissible by the user, auto-removes after a few seconds.
    /// </summary>
    public sealed class InlineToastViewModel : ReactiveObject
    {
        public string Title { get; }
        public string Message { get; }
        public ToastType Type { get; }

        /// <summary>fa-* icon for the toast's leading glyph.</summary>
        public string Icon { get; }

        /// <summary>Theme brush key resolved by the view for the accent color.</summary>
        public string BrushKey { get; }

        public InlineToastViewModel(string title, string message, ToastType type)
        {
            Title = title;
            Message = message;
            Type = type;

            (Icon, BrushKey) = type switch
            {
                ToastType.Success => ("fa-circle-check", "BrushTextSuccess"),
                ToastType.Error => ("fa-triangle-exclamation", "BrushTextError"),
                ToastType.Warning => ("fa-triangle-exclamation", "BrushTextWarning"),
                _ => ("fa-info-circle", "BrushTextPrimary")
            };
        }
    }
}
