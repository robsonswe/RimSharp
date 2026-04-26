// Features/WorkshopDownloader/Components/Browser/WindowsBrowserControl.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class WindowsBrowserControl : IBrowserControl
    {
        private readonly WebView2 _webView;
        private bool _isNavigating;
        private bool _isDomReady;

        public bool IsNavigating => _isNavigating;
        public bool IsDomReady => _isDomReady;

        public WindowsBrowserControl(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));

            _webView.NavigationStarting += OnWebViewNavigationStarting;
            _webView.NavigationCompleted += OnWebViewNavigationCompleted;

            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.DOMContentLoaded += OnWebViewDomContentLoaded;
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            }
            else
            {
                _webView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess && _webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.DOMContentLoaded += OnWebViewDomContentLoaded;
                        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                    }
                };
            }
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            _webView.CoreWebView2.Navigate(e.Uri);
        }

        private void OnWebViewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _isNavigating = true;
            _isDomReady = false;

            var args = new NavigationStartingEventArgs(e.Uri);
            NavigationStarting?.Invoke(this, args);

            if (args.Cancel)
            {
                e.Cancel = true;
                _isNavigating = false;
                LoadingStateChanged?.Invoke(this, false);
            }
            else
            {
                LoadingStateChanged?.Invoke(this, true);
            }
        }

        private void OnWebViewDomContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            _isDomReady = true;
            Debug.WriteLine($"[WindowsBrowserControl] DOMContentLoaded fired for: {_webView.Source}");

            DomContentLoaded?.Invoke(this, _webView.Source?.ToString() ?? string.Empty);
        }

        private void OnWebViewNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _isNavigating = false;
            var url = _webView.Source?.ToString() ?? string.Empty;
            Debug.WriteLine($"[WindowsBrowserControl] NavigationCompleted: {url}, Success: {e.IsSuccess}");
            NavigationCompleted?.Invoke(this, url);
            LoadingStateChanged?.Invoke(this, false);
        }

        public string? Source
        {
            get => _webView.Source?.ToString();
            set => _webView.Source = value != null ? new Uri(value) : null;
        }

        public bool CanGoBack => _webView.CanGoBack;
        public bool CanGoForward => _webView.CanGoForward;

        public void GoBack() => _webView.GoBack();
        public void GoForward() => _webView.GoForward();
        public void Reload() => _webView.Reload();
        public void Navigate(string url) => _webView.Source = new Uri(url);

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (_webView.CoreWebView2 == null) return "";
            return await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        public event EventHandler<NavigationStartingEventArgs>? NavigationStarting;
        public event EventHandler<string>? NavigationCompleted;
        public event EventHandler<string>? DomContentLoaded;
        public event EventHandler<bool>? LoadingStateChanged;
    }
}
