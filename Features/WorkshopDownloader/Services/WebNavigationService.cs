#nullable enable
using System;
using System.Diagnostics;
using System.Web;

namespace RimSharp.Features.WorkshopDownloader.Services
{
        public interface IWebNavigationService
        {
            void SetBrowserControl(RimSharp.Features.WorkshopDownloader.Components.Browser.IBrowserControl? browserControl);
            void GoBack();
            void GoForward();
            void GoHome();
            void Navigate(string url);
            bool CanGoBack { get; }
            bool CanGoForward { get; }
            bool IsNavigating { get; }
            bool IsDomReady { get; }
            string CurrentUrl { get; }
            string? IntendedUrl { get; }
    
            event EventHandler<string>? SourceUrlChanged;
            event EventHandler<string>? StatusChanged;
            event EventHandler? NavigationStateChanged;
    
            event EventHandler<string>? NavigationStarted;
            event EventHandler? NavigationEnded;
            event EventHandler<string>? PotentialWorkshopPageLoaded;
            event EventHandler<string>? DomContentLoaded;
        }
    
        public class WebNavigationService : IWebNavigationService, IDisposable
        {
            private RimSharp.Features.WorkshopDownloader.Components.Browser.IBrowserControl? _browserControl;
    
            private bool _canGoBack;
            private bool _canGoForward;
            private string _currentUrl = string.Empty;
            private string? _intendedUrl;
    
            public bool CanGoBack
            {
                get => _canGoBack;
                private set
                {
                    if (_canGoBack != value)
                    {
                        _canGoBack = value;
                        OnNavigationStateChanged();
                    }
                }
            }
    
            public bool CanGoForward
            {
                get => _canGoForward;
                private set
                {
                    if (_canGoForward != value)
                    {
                        _canGoForward = value;
                        OnNavigationStateChanged();
                    }
                }
            }
    
            public bool IsNavigating => _browserControl?.IsNavigating ?? false;
            public bool IsDomReady => _browserControl?.IsDomReady ?? false;
    
            public string CurrentUrl
            {
                get => _currentUrl;
                private set
                {
                    if (_currentUrl != value && !string.IsNullOrEmpty(value))
                    {
                        _currentUrl = value;
                    }
                }
            }
    
            public string? IntendedUrl
            {
                get => _intendedUrl;
                private set
                {
                    if (_intendedUrl != value)
                    {
                        _intendedUrl = value;
                    }
                }
            }
    
            public event EventHandler<string>? SourceUrlChanged;
            public event EventHandler<string>? StatusChanged;
            public event EventHandler? NavigationStateChanged;
            public event EventHandler<string>? NavigationStarted;
            public event EventHandler? NavigationEnded;
            public event EventHandler<string>? PotentialWorkshopPageLoaded;
            public event EventHandler<string>? DomContentLoaded;
    
            public void SetBrowserControl(RimSharp.Features.WorkshopDownloader.Components.Browser.IBrowserControl? browserControl)
            {
                if (_browserControl == browserControl) return;
    
                if (_browserControl != null)
                {
                    _browserControl.NavigationStarting -= OnNavigationStarting;
                    _browserControl.NavigationCompleted -= OnNavigationCompleted;
                    _browserControl.DomContentLoaded -= OnDomContentLoaded;
                }
    
                _browserControl = browserControl;
    
                if (_browserControl != null)
                {
                    _browserControl.NavigationStarting += OnNavigationStarting;
                    _browserControl.NavigationCompleted += OnNavigationCompleted;
                    _browserControl.DomContentLoaded += OnDomContentLoaded;
                    UpdateNavigationState();
                    CurrentUrl = _browserControl.Source ?? string.Empty;
                    SourceUrlChanged?.Invoke(this, CurrentUrl);
                }
            }
    

        private void OnNavigationStarting(object? sender, string url)
        {
            IntendedUrl = url;
            StatusChanged?.Invoke(this, $"Loading: {url}");
            NavigationStarted?.Invoke(this, url);
        }

        private void OnNavigationCompleted(object? sender, string url)
        {
            CurrentUrl = url;
            IntendedUrl = null;
            UpdateNavigationState();

            if (IsPotentiallyWorkshopPage(url))
            {
                PotentialWorkshopPageLoaded?.Invoke(this, url);
            }

            StatusChanged?.Invoke(this, $"Loaded: {url}");
            NavigationEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnDomContentLoaded(object? sender, string url)
        {
            Debug.WriteLine($"[WebNavigationService] DOMContentLoaded for: {url}");
            
            // Signal that navigation has reached a usable state early (perf: 3a2287e)
            NavigationEnded?.Invoke(this, EventArgs.Empty);

            DomContentLoaded?.Invoke(this, url);
            
            // Also check if this is a workshop page when DOM is ready
            if (IsPotentiallyWorkshopPage(url))
            {
                PotentialWorkshopPageLoaded?.Invoke(this, url);
            }
        }

        public void Navigate(string url) => _browserControl?.Navigate(url);

        public void GoBack() => _browserControl?.GoBack();

        public void GoForward() => _browserControl?.GoForward();

        public void GoHome()
        {
            const string homeUrl = "https://steamcommunity.com/app/294100/workshop/";
            Navigate(homeUrl);
        }

        private void UpdateNavigationState()
        {
            if (_browserControl != null)
            {
                CanGoBack = _browserControl.CanGoBack;
                CanGoForward = _browserControl.CanGoForward;
                OnNavigationStateChanged();
            }
        }

        private void OnNavigationStateChanged() => NavigationStateChanged?.Invoke(this, EventArgs.Empty);

        private bool IsPotentiallyWorkshopPage(string? url)
        {
            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                bool isSteamWorkshop = uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase);
                bool hasFileDetailsPath = uri.AbsolutePath.Contains("/sharedfiles/filedetails/", StringComparison.OrdinalIgnoreCase);
                bool hasWorkshopPath = uri.AbsolutePath.Contains("/workshop/filedetails/", StringComparison.OrdinalIgnoreCase);
                string? id = HttpUtility.ParseQueryString(uri.Query).Get("id");
                return isSteamWorkshop && !string.IsNullOrEmpty(id) && (hasFileDetailsPath || hasWorkshopPath);
            }
            return false;
        }

        public void Dispose()
        {
            if (_browserControl != null)
            {
                _browserControl.NavigationStarting -= OnNavigationStarting;
                _browserControl.NavigationCompleted -= OnNavigationCompleted;
            }
        }

        // Backward compatibility
        public void SetWebView(Microsoft.Web.WebView2.WinForms.WebView2? webView) 
        {
            if (webView == null)
            {
                SetBrowserControl(null);
            }
            else
            {
                SetBrowserControl(new RimSharp.Features.WorkshopDownloader.Components.Browser.WindowsBrowserControl(webView));
            }
        }
    }
}
