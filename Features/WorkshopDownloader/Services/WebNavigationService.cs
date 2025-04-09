using System;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
// Use the WPF version of WebView2
using Microsoft.Web.WebView2.Wpf; // <<< CHANGED

namespace RimSharp.Features.WorkshopDownloader.Services
{
    // Interface remains the same
    public interface IWebNavigationService
    {
        void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView); // <<< CHANGED TYPE
        void GoBack();
        void GoForward();
        void GoHome();
        bool CanGoBack { get; }
        bool CanGoForward { get; }
        bool IsValidModUrl { get; }
        string CurrentUrl { get; }


        event EventHandler<string> StatusChanged;
        event EventHandler NavigationStateChanged;
        event EventHandler<bool> ModUrlValidityChanged;
        event EventHandler<string> NavigationSucceededAndUrlValid; 
    }

    public class WebNavigationService : IWebNavigationService
    {
        // Use the WPF WebView2 type
        private Microsoft.Web.WebView2.Wpf.WebView2 _webView; // <<< CHANGED

        private bool _canGoBack; // Backing fields for properties
        private bool _canGoForward;
        private bool _isValidModUrl;
        private ModExtractorService _extractorService;



        public bool CanGoBack
        {
            get => _canGoBack;
            private set
            {
                if (_canGoBack != value)
                {
                    _canGoBack = value;
                    NavigationStateChanged?.Invoke(this, EventArgs.Empty); // Notify on change
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
                    NavigationStateChanged?.Invoke(this, EventArgs.Empty); // Notify on change
                }
            }
        }

        public bool IsValidModUrl
        {
            get => _isValidModUrl;
            private set
            {
                if (_isValidModUrl != value)
                {
                    _isValidModUrl = value;
                    ModUrlValidityChanged?.Invoke(this, _isValidModUrl); // Notify on change
                }
            }
        }

        public string CurrentUrl => _webView?.Source?.ToString();

        public event EventHandler<string> StatusChanged;
        public event EventHandler NavigationStateChanged; // Now invoked by property setters
        public event EventHandler<bool> ModUrlValidityChanged; // Now invoked by property setter
        public event EventHandler<string> NavigationSucceededAndUrlValid;

        // Accept the WPF WebView2 type
        public void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView) // <<< CHANGED
        {
            // Unsubscribe from old instance if any
            if (_webView != null)
            {
                _webView.NavigationStarting -= WebView_NavigationStarting;
                _webView.NavigationCompleted -= WebView_NavigationCompleted;
                _webView.SourceChanged -= WebView_SourceChanged;
                _webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted;
            }

            _webView = webView;
            _extractorService = new ModExtractorService(webView);

            if (_webView != null)
            {
                // Prefer waiting for CoreWebView2 to be ready if it isn't already
                if (_webView.CoreWebView2 == null)
                {
                    _webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
                    // Ensure initialization is triggered if not done externally (e.g., by setting Source in XAML)
                    // It's often better to let the View handle EnsureCoreWebView2Async or rely on XAML Source.
                    // _webView.EnsureCoreWebView2Async(); // Consider if needed here vs. View
                }
                else
                {
                    // If CoreWebView2 is already available, hook events immediately
                    HookCoreWebView2Events();
                    UpdateNavigationState(); // Update state based on current CoreWebView2
                    CheckUrlValidity(_webView.Source?.ToString()); // Check initial URL
                }
            }
        }

        private void WebView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                HookCoreWebView2Events();
                UpdateNavigationState(); // Update state now that CoreWebView2 is ready
                CheckUrlValidity(_webView.Source?.ToString()); // Check initial URL
            }
            else
            {
                StatusChanged?.Invoke(this, $"WebView2 Core Initialization Failed: {e.InitializationException}");
            }
        }

        private void HookCoreWebView2Events()
        {
            if (_webView?.CoreWebView2 == null) return;

            // Unsubscribe first to prevent duplicates if called multiple times
            _webView.NavigationStarting -= WebView_NavigationStarting;
            _webView.NavigationCompleted -= WebView_NavigationCompleted;
            _webView.SourceChanged -= WebView_SourceChanged;

            // Subscribe to events
            _webView.NavigationStarting += WebView_NavigationStarting;
            _webView.NavigationCompleted += WebView_NavigationCompleted;
            // SourceChanged is useful for detecting navigations initiated by script or user typing URL
            _webView.SourceChanged += WebView_SourceChanged;
        }

        private void WebView_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            // This event fires when the Source property itself changes.
            // It's a good place to update navigation state as well.
            UpdateNavigationState();
            CheckUrlValidity(_webView?.Source?.ToString());
        }


        public void GoBack()
        {
            try
            {
                if (_webView?.CoreWebView2 != null && _webView.CoreWebView2.CanGoBack)
                {
                    _webView.CoreWebView2.GoBack();
                    // Navigation state will update via NavigationCompleted/SourceChanged
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error going back: {ex.Message}");
            }
        }

        public void GoForward()
        {
            try
            {
                if (_webView?.CoreWebView2 != null && _webView.CoreWebView2.CanGoForward)
                {
                    _webView.CoreWebView2.GoForward();
                    // Navigation state will update via NavigationCompleted/SourceChanged
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error going forward: {ex.Message}");
            }
        }

        public void GoHome()
        {
            try
            {
                const string homeUrl = "https://steamcommunity.com/app/294100/workshop/";
                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Navigate(homeUrl);
                    // Navigation state will update via NavigationStarting/Completed/SourceChanged
                }
                else if (_webView != null)
                {
                    // If CoreWebView2 isn't ready yet, set the Source property
                    // This might trigger initialization if EnsureCoreWebView2Async hasn't been called
                    _webView.Source = new Uri(homeUrl);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error navigating home: {ex.Message}");
            }
        }

        private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            StatusChanged?.Invoke(this, $"Loading: {e.Uri}");
            // Check validity early, but final decision often waits for completion
            // CheckUrlValidity(e.Uri);
        }

                private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            UpdateNavigationState(); // Update CanGoBack/Forward

            string currentSource = _webView?.Source?.ToString() ?? "Unknown URL";
            string status;

            // Check validity AFTER navigation attempt completes
            // This will set the IsValidModUrl property and raise ModUrlValidityChanged event
            CheckUrlValidity(currentSource);

            if (e.IsSuccess)
            {
                status = $"Loaded: {currentSource}";
                // *** If successful AND the URL is now considered valid, raise the new event ***
                if (IsValidModUrl) // Check the property state after CheckUrlValidity ran
                {
                    Debug.WriteLine($"[WebNavService] Navigation succeeded for valid URL: {currentSource}. Raising NavigationSucceededAndUrlValid.");
                    NavigationSucceededAndUrlValid?.Invoke(this, currentSource); // Raise the new event
                }
                else
                {
                     Debug.WriteLine($"[WebNavService] Navigation succeeded but URL is not a valid mod URL: {currentSource}");
                }
            }
            else
            {
                status = $"Failed to load: {currentSource} (Error: {e.WebErrorStatus})";
                Debug.WriteLine($"[WebNavService] Navigation failed for URL: {currentSource}");
                // IsValidModUrl would have been set by CheckUrlValidity above, possibly to false if URL invalid, or maybe still true if it was valid but failed to load
            }

            StatusChanged?.Invoke(this, status);
        }



        private void UpdateNavigationState()
        {
            // Use Dispatcher if updating UI-bound properties from a non-UI thread,
            // but CanGoBack/CanGoForward are typically checked on the UI thread for commands.
            // However, WebView2 events might come back on different threads.
            // Safest is to marshal back if there's doubt, but often unnecessary for simple bools
            // that are read later by UI thread commands. Let's assume direct update is okay for now.

            bool coreWebViewAvailable = _webView?.CoreWebView2 != null;
            CanGoBack = coreWebViewAvailable && _webView.CoreWebView2.CanGoBack;
            CanGoForward = coreWebViewAvailable && _webView.CoreWebView2.CanGoForward;

            // No need to invoke NavigationStateChanged here, the property setters do it.
        }

        private void CheckUrlValidity(string url)
        {
            // Use Uri for more robust checking
            bool isValid = false;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                isValid = uri.Host.EndsWith("steamcommunity.com") &&
                          uri.AbsolutePath.Contains("/sharedfiles/filedetails/") &&
                          !string.IsNullOrEmpty(System.Web.HttpUtility.ParseQueryString(uri.Query).Get("id"));
            }
            IsValidModUrl = isValid;
            // No need to invoke ModUrlValidityChanged here, the property setter does it.
        }
    }
}
