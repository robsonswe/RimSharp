// Features/WorkshopDownloader/Components/Browser/IBrowserControl.cs
#nullable enable
using System;
using System.Threading.Tasks;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public interface IBrowserControl
    {
        string? Source { get; set; }
        bool CanGoBack { get; }
        bool CanGoForward { get; }
        bool IsNavigating { get; }
        bool IsDomReady { get; }

        void GoBack();
        void GoForward();
        void Reload();
        void Navigate(string url);

        Task<string> ExecuteScriptAsync(string script);

        event EventHandler<string>? NavigationStarting;
        event EventHandler<string>? NavigationCompleted;
        event EventHandler<string>? DomContentLoaded;
        event EventHandler<bool>? LoadingStateChanged;
    }
}
