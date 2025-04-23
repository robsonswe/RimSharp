#nullable enable
using System;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Web; // Required for HttpUtility if you use it in CheckUrlValidity

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public interface IWebNavigationService
    {
        void SetWebView(WebView2 webView);
        void GoBack();
        void GoForward();
        void GoHome();
        void Navigate(string url); // Added for explicit navigation requests
        bool CanGoBack { get; }
        bool CanGoForward { get; }
        string CurrentUrl { get; } // Represents the *actual* current URL after completion/source change
        string? IntendedUrl { get; } // Represents the URL being navigated *to*

        event EventHandler<string> SourceUrlChanged; // Raised when the Source property actually changes (or potentially on start)
        event EventHandler<string> StatusChanged;
        event EventHandler NavigationStateChanged;

        // --- New Events for Loading State ---
        event EventHandler<string> NavigationStarted; // Passes the intended URL
        event EventHandler NavigationEnded; // Signals completion or failure
        event EventHandler<string> PotentialWorkshopPageLoaded; // Raised when a potential workshop page finishes loading successfully

        // --- New Window Handling Event ---
        event EventHandler<string> NewWindowNavigationRequested;
    }

    public class WebNavigationService : IWebNavigationService, IDisposable
    {
        private WebView2? _webView;

        private bool _canGoBack;
        private bool _canGoForward;
        private string _currentUrl = string.Empty; // Backing field for CurrentUrl property
        private string? _intendedUrl; // Backing field for IntendedUrl property

        // --- Properties ---
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

        public string CurrentUrl
        {
            get => _currentUrl;
            private set
            {
                // Only update if truly different and not null/empty unless intended
                if (_currentUrl != value && !string.IsNullOrEmpty(value))
                {
                    _currentUrl = value;
                    Debug.WriteLine($"[WebNavService] CurrentUrl updated to: {value}");
                    // Maybe raise SourceUrlChanged here *if* it represents the final confirmed URL
                    // SourceUrlChanged?.Invoke(this, _currentUrl); // Let's stick to SourceChanged event for this
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
                    Debug.WriteLine($"[WebNavService] IntendedUrl set to: {value ?? "null"}");
                }
            }
        }

        // --- Events ---
        public event EventHandler<string>? SourceUrlChanged;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? NavigationStateChanged;
        public event EventHandler<string>? NavigationStarted; // Passes intended URL
        public event EventHandler? NavigationEnded;
        public event EventHandler<string>? PotentialWorkshopPageLoaded;
        public event EventHandler<string>? NewWindowNavigationRequested; // For handling new windows

        public void SetWebView(WebView2 webView)
        {
            if (_webView == webView) return; // No change

            DisposeCurrentWebViewEvents(); // Unsubscribe from old one

            _webView = webView ?? throw new ArgumentNullException(nameof(webView));


            if (_webView.CoreWebView2 == null)
            {
                Debug.WriteLine("[WebNavService] CoreWebView2 not ready, attaching handler.");
                _webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
                // Ensure initialization is triggered (often done by View's EnsureCoreWebView2Async)
            }
            else
            {
                Debug.WriteLine("[WebNavService] CoreWebView2 ready, hooking events immediately.");
                HookCoreWebView2Events();
                UpdateNavigationState();
                CurrentUrl = _webView.Source?.ToString() ?? string.Empty; // Update initial confirmed URL
                SourceUrlChanged?.Invoke(this, CurrentUrl); // Raise initial URL change
            }
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            // Unsubscribe to prevent multiple calls if SetWebView is called again before init completes
            if (_webView != null) _webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted;

            if (e.IsSuccess && _webView?.CoreWebView2 != null)
            {
                Debug.WriteLine("[WebNavService] CoreWebView2InitializationCompleted successfully.");
                HookCoreWebView2Events();
                UpdateNavigationState();
                CurrentUrl = _webView.Source?.ToString() ?? string.Empty; // Update confirmed URL
                SourceUrlChanged?.Invoke(this, CurrentUrl); // Raise initial URL change
            }
            else
            {
                Debug.WriteLine($"[WebNavService] CoreWebView2 Initialization Failed: {e.InitializationException}");
                StatusChanged?.Invoke(this, $"WebView2 Core Initialization Failed: {e.InitializationException?.Message}");
            }
        }

        private void HookCoreWebView2Events()
        {
            if (_webView?.CoreWebView2 == null)
            {
                Debug.WriteLine("[WebNavService] HookCoreWebView2Events: Attempted to hook but CoreWebView2 is null.");
                return;
            }
            Debug.WriteLine("[WebNavService] Hooking CoreWebView2 events...");

            // Unsubscribe first
            _webView.CoreWebView2.NavigationStarting -= WebView_NavigationStarting;
            _webView.CoreWebView2.NavigationCompleted -= WebView_NavigationCompleted;
            _webView.CoreWebView2.SourceChanged -= WebView_SourceChanged; // Renamed from WebView_SourceChanged
            _webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested; // Add NewWindowRequested

            // Subscribe
            _webView.CoreWebView2.NavigationStarting += WebView_NavigationStarting;
            _webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;
            _webView.CoreWebView2.SourceChanged += WebView_SourceChanged; // Renamed from WebView_SourceChanged
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested; // Add NewWindowRequested

            Debug.WriteLine("[WebNavService] CoreWebView2 events hooked.");
        }

        private void DisposeCurrentWebViewEvents()
        {
            if (_webView?.CoreWebView2 != null)
            {
                Debug.WriteLine("[WebNavService] Unhooking CoreWebView2 events from previous WebView...");
                _webView.CoreWebView2.NavigationStarting -= WebView_NavigationStarting;
                _webView.CoreWebView2.NavigationCompleted -= WebView_NavigationCompleted;
                _webView.CoreWebView2.SourceChanged -= WebView_SourceChanged;
                _webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                _webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted; // Also unsubscribe this
                Debug.WriteLine("[WebNavService] Unhooked CoreWebView2 events.");
            }
            else if (_webView != null)
            {
                _webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted; // Still need to unsub this if CoreWebView2 was null
            }
            _webView = null; // Clear the reference after unsubscribing
        }

        // --- Event Handlers ---

        private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string targetUrl = e.Uri ?? "about:blank";
            Debug.WriteLine($"[WebNavService] NavigationStarting: {targetUrl}");
            IntendedUrl = targetUrl; // Set the intended URL
            StatusChanged?.Invoke(this, $"Loading: {targetUrl}");
            NavigationStarted?.Invoke(this, targetUrl); // Raise NavigationStarted with the URL
        }

        private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // This event confirms the outcome of a navigation attempt initiated by Navigate or user action.
            string finalUrl = _webView?.CoreWebView2?.Source ?? _intendedUrl ?? "Unknown URL"; // Use CoreWebView2.Source as the most reliable final URL
            Debug.WriteLine($"[WebNavService] NavigationCompleted: Success={e.IsSuccess}, Status={e.WebErrorStatus}, Final URL={finalUrl}");

            CurrentUrl = finalUrl; // Update the confirmed current URL
            IntendedUrl = null; // Clear intended URL after completion
            UpdateNavigationState(); // Update CanGoBack/Forward

            string status;
            if (e.IsSuccess)
            {
                status = $"Loaded: {finalUrl}";
                // --- NEW: Check if it's potentially a workshop page and raise event ---
                if (IsPotentiallyWorkshopPage(finalUrl))
                {
                    Debug.WriteLine($"[WebNavService] Navigation succeeded for potentially valid workshop page: {finalUrl}. Raising PotentialWorkshopPageLoaded.");
                    PotentialWorkshopPageLoaded?.Invoke(this, finalUrl); // <<< RAISE NEW EVENT
                }
                else
                {
                    Debug.WriteLine($"[WebNavService] Navigation succeeded, but URL is not a potential workshop page: {finalUrl}");
                }
                // --- End NEW ---
            }
            else
            {
                // Ignore specific errors like cancellation or navigation aborts if needed
                if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    status = $"Failed to load: {finalUrl} (Error: {e.WebErrorStatus})";
                    Debug.WriteLine($"[WebNavService] Navigation failed for URL: {finalUrl}");
                }
                else
                {
                    status = $"Navigation cancelled/aborted: {finalUrl}";
                    Debug.WriteLine($"[WebNavService] Navigation cancelled/aborted for URL: {finalUrl}");
                }
            }
            StatusChanged?.Invoke(this, status);
            NavigationEnded?.Invoke(this, EventArgs.Empty); // Signal navigation attempt has finished
        }

        private void WebView_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            // This fires when the CoreWebView2.Source property changes, AFTER navigation is successful
            // or due to history navigation (back/forward).
            // Avoid redundant updates if NavigationCompleted already handled it.
            string newSource = _webView?.CoreWebView2?.Source ?? string.Empty;
            Debug.WriteLine($"[WebNavService] SourceChanged: IsNewDocument={e.IsNewDocument}, New Source={newSource}");

            if (CurrentUrl != newSource)
            {
                CurrentUrl = newSource;
                UpdateNavigationState();
                SourceUrlChanged?.Invoke(this, newSource);
                Debug.WriteLine($"[WebNavService] Raised SourceUrlChanged from SourceChanged handler: {newSource}");
                if (IsPotentiallyWorkshopPage(newSource))
                {
                    Debug.WriteLine($"[WebNavService] SourceChanged to potentially valid workshop page: {newSource}. Raising PotentialWorkshopPageLoaded.");
                    PotentialWorkshopPageLoaded?.Invoke(this, newSource); // <<< RAISE NEW EVENT
                }
            }
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            string targetUri = e.Uri;
            Debug.WriteLine($"[WebNavService] NewWindowRequested for: {targetUri}. Handling in current view.");
            StatusChanged?.Invoke(this, $"Opening link in current view: {targetUri}...");

            // Prevent the default new window behavior
            e.Handled = true;

            // Raise an event for the ViewModel or navigate directly if preferred
            // Raising an event gives more control to the VM/View layer
            // NewWindowNavigationRequested?.Invoke(this, targetUri);

            // --- OR --- Navigate directly within this service (simpler for now)
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate(targetUri);
            }
            else
            {
                Debug.WriteLine("[WebNavService] Cannot navigate new window request: CoreWebView2 is null.");
                StatusChanged?.Invoke(this, $"Error: Cannot navigate - browser not fully initialized.");
            }
        }


        // --- Actions ---

        public void Navigate(string url)
        {
            if (_webView?.CoreWebView2 != null)
            {
                try
                {
                    _webView.CoreWebView2.Navigate(url);
                    // IntendedUrl and NavigationStarted event will be set by WebView_NavigationStarting handler
                }
                catch (ArgumentException ex)
                {
                    Debug.WriteLine($"[WebNavService] Invalid URL format for navigation: {url} - {ex.Message}");
                    StatusChanged?.Invoke(this, $"Invalid URL format: {url}");
                    NavigationEnded?.Invoke(this, EventArgs.Empty); // Ensure loading state resets on error
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebNavService] Navigation error for URL {url}: {ex.Message}");
                    StatusChanged?.Invoke(this, $"Error navigating: {ex.Message}");
                    NavigationEnded?.Invoke(this, EventArgs.Empty); // Ensure loading state resets on error
                }
            }
            else if (_webView != null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Fallback - less ideal as it bypasses CoreWebView2 events slightly
                Debug.WriteLine("[WebNavService] Warning: Navigating via Source property (CoreWebView2 might not be ready).");
                _webView.Source = uri; // This will trigger SourceChanged eventually
                IntendedUrl = url; // Manually set intended URL
                NavigationStarted?.Invoke(this, url); // Manually raise started event
            }
            else
            {
                Debug.WriteLine("[WebNavService] WebView or CoreWebView2 not available/initialized, cannot navigate.");
                StatusChanged?.Invoke(this, "Browser component not ready for navigation.");
            }
        }


        public void GoBack()
        {
            try
            {
                if (_webView?.CoreWebView2?.CanGoBack ?? false)
                {
                    _webView.CoreWebView2.GoBack();
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error going back: {ex.Message}");
                NavigationEnded?.Invoke(this, EventArgs.Empty); // Ensure loading resets on error
            }
        }

        public void GoForward()
        {
            try
            {
                if (_webView?.CoreWebView2?.CanGoForward ?? false)
                {
                    _webView.CoreWebView2.GoForward();
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error going forward: {ex.Message}");
                NavigationEnded?.Invoke(this, EventArgs.Empty); // Ensure loading resets on error
            }
        }

        public void GoHome()
        {
            const string homeUrl = "https://steamcommunity.com/app/294100/workshop/";
            Navigate(homeUrl); // Use the common Navigate method
        }

        // --- Helpers ---

        private void UpdateNavigationState()
        {
            // Use Dispatcher? Not strictly necessary if only read by UI thread later, but safer.
            // Application.Current?.Dispatcher.Invoke(() => { ... }); // If needed

            bool coreAvailable = _webView?.CoreWebView2 != null;
            CanGoBack = coreAvailable && _webView!.CoreWebView2.CanGoBack;
            CanGoForward = coreAvailable && _webView!.CoreWebView2.CanGoForward;
            // Debug logging happens in property setters now via OnNavigationStateChanged
        }

        // Wrapper to ensure event is raised correctly
        private void OnNavigationStateChanged()
        {
            Debug.WriteLine($"[WebNavService] Raising NavigationStateChanged. CanGoBack={CanGoBack}, CanGoForward={CanGoForward}");
            NavigationStateChanged?.Invoke(this, EventArgs.Empty);
        }


        private bool IsPotentiallyWorkshopPage(string? url)
        {
            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                bool isSteamWorkshop = uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase);
                // Check for *either* path segment associated with items/collections
                bool hasFileDetailsPath = uri.AbsolutePath.Contains("/sharedfiles/filedetails/", StringComparison.OrdinalIgnoreCase);
                bool hasWorkshopPath = uri.AbsolutePath.Contains("/workshop/filedetails/", StringComparison.OrdinalIgnoreCase);
                string? id = HttpUtility.ParseQueryString(uri.Query).Get("id");
                bool hasId = !string.IsNullOrEmpty(id);

                // Needs to be steam, have an ID, and contain one of the known path segments
                bool isPotential = isSteamWorkshop && hasId && (hasFileDetailsPath || hasWorkshopPath);
                Debug.WriteLine($"[WebNavService] Potential Workshop Page Check for '{url}': {isPotential}");
                return isPotential;
            }
            Debug.WriteLine($"[WebNavService] Potential Workshop Page Check for '{url}': False (invalid URL)");
            return false;
        }
        // --- IDisposable ---
        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    Debug.WriteLine("[WebNavService] Disposing...");
                    DisposeCurrentWebViewEvents(); // Unhook events
                    // _extractorService?.Dispose(); // ViewModel owns this now
                    // _extractorService = null;
                }
                _disposed = true;
                Debug.WriteLine("[WebNavService] Dispose finished.");
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        ~WebNavigationService()
        {
            Dispose(false);
        }
    }
}
