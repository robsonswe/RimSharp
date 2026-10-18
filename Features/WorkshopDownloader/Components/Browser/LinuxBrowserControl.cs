// Features/WorkshopDownloader/Components/Browser/LinuxBrowserControl.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    /// <summary>
    /// Linux implementation of <see cref="IBrowserControl"/> wrapping the
    /// Avalonia <see cref="NativeWebView"/> control (WebKitGTK 4.1 / WPE backend).
    /// Mirrors the event semantics of <see cref="WindowsBrowserControl"/>.
    /// </summary>
    public class LinuxBrowserControl : IBrowserControl
    {
        private const string Module = "LinuxBrowserControl";
        private const string DomReadyMessage = "rimsharp:domContentLoaded";

        // Injected on every NavigationStarted. Registers a DOMContentLoaded/load
        // listener that posts a web message through the WebKitGTK script message
        // handler ("postAvWebViewMessage", registered by the Avalonia adapter),
        // which surfaces as NativeWebView.WebMessageReceived.
        // NOTE: deliberately does NOT post when the document is already
        // interactive/complete - if the injection lands in a stale document
        // context (navigation just started, new document not committed yet) the
        // old document would immediately "fire" a bogus DOMContentLoaded. The
        // readyState polling fallback covers the already-loaded case instead.
        private const string DomReadyInjectionScript =
@"(function() {
    if (window.__rimsharpDomReadyHookInstalled) return;
    window.__rimsharpDomReadyHookInstalled = true;
    var post = function() {
        try {
            window.webkit.messageHandlers.postAvWebViewMessage.postMessage('rimsharp:domContentLoaded');
        } catch (e) {}
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', post, { once: true });
        window.addEventListener('load', post, { once: true });
    }
})();";

        private const string ReadyStateScript = "document.readyState";

        private readonly NativeWebView _webView;
        private readonly ILoggerService? _logger;
        private bool _isNavigating;
        private bool _isDomReady;
        private bool _domReadyRaisedForCurrentNavigation;
        private string? _currentNavigationUrl;
        private string? _domReadyTier;
        private CancellationTokenSource? _domReadyPollCts;

        public bool IsNavigating => _isNavigating;
        public bool IsDomReady => _isDomReady;

        public LinuxBrowserControl(NativeWebView webView, ILoggerService? logger = null)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _logger = logger;
            _logger?.LogDebug("LinuxBrowserControl created.", Module);

            _webView.NavigationStarted += OnWebViewNavigationStarted;
            _webView.NavigationCompleted += OnWebViewNavigationCompleted;
            _webView.NewWindowRequested += OnWebViewNewWindowRequested;
            _webView.WebMessageReceived += OnWebViewWebMessageReceived;
        }

        private void OnWebViewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            _isNavigating = true;
            _isDomReady = false;
            _domReadyRaisedForCurrentNavigation = false;
            _domReadyTier = null;
            _currentNavigationUrl = e.Request?.ToString() ?? _webView.Source?.ToString() ?? string.Empty;
            _logger?.LogInfo($"NavigationStarted: {_currentNavigationUrl}", Module);

            var args = new NavigationStartingEventArgs(_currentNavigationUrl);
            NavigationStarting?.Invoke(this, args);

            if (args.Cancel)
            {
                e.Cancel = true;
                _isNavigating = false;
                StopDomReadyPolling();
                _logger?.LogInfo($"NavigationStarting CANCELLED by consumer: {_currentNavigationUrl}", Module);
                LoadingStateChanged?.Invoke(this, false);
            }
            else
            {
                LoadingStateChanged?.Invoke(this, true);
                _ = BeginDomReadyDetectionAsync(_currentNavigationUrl);
            }
        }

        private void OnWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            _isNavigating = false;
            var url = _webView.Source?.ToString() ?? e.Request?.ToString() ?? string.Empty;
            _logger?.LogInfo($"NavigationCompleted: {url}, Success: {e.IsSuccess}", Module);
            NavigationCompleted?.Invoke(this, url);
            LoadingStateChanged?.Invoke(this, false);

            // Safety net: NativeWebView has no native DOMContentLoaded event. If
            // neither the web-message bridge nor the readyState poll reported it
            // by the time the load finished, the DOM is ready by definition now.
            if (e.IsSuccess && !_domReadyRaisedForCurrentNavigation)
            {
                _domReadyTier = "navigation-completed-fallback";
                RaiseDomContentLoaded(url);
            }
            StopDomReadyPolling();
        }

        private void OnWebViewNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
        {
            // Mirror WindowsBrowserControl: suppress the new window and navigate
            // in place so the ViewModel's external-link confirmation applies.
            e.Handled = true;
            if (e.Request != null)
            {
                _logger?.LogInfo($"NewWindowRequested -> navigating in place: {e.Request}", Module);
                _webView.Navigate(e.Request);
            }
        }

        private void OnWebViewWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
        {
            if (!string.Equals(e.Body, DomReadyMessage, StringComparison.Ordinal)) return;
            _domReadyTier = "web-message-bridge";
            RaiseDomContentLoaded(_currentNavigationUrl ?? _webView.Source?.ToString() ?? string.Empty);
        }

        // NativeWebView has no DOMContentLoaded event - derive it:
        // 1. Preferred: inject a DOMContentLoaded listener (see
        //    DomReadyInjectionScript) that posts a web message consumed above.
        // 2. Fallback: poll document.readyState via InvokeScript until the
        //    document is interactive/complete (transient evaluation errors
        //    during navigation are swallowed), with a timeout.
        // 3. Last resort: NavigationCompleted raises it (see above).
        private async Task BeginDomReadyDetectionAsync(string url)
        {
            StopDomReadyPolling();
            var cts = new CancellationTokenSource();
            _domReadyPollCts = cts;

            // 1. Fire-and-forget listener injection. May land in the previous
            //    document's context if the backend raises NavigationStarted
            //    before the new document is committed; harmless (the guard in
            //    the script prevents stale posts), the poll covers that case.
            try
            {
                await _webView.InvokeScript(DomReadyInjectionScript);
                _logger?.LogDebug("DOM-ready listener injected.", Module);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"DOM-ready listener injection failed (polling fallback active): {ex.Message}", Module);
            }

            // 2. Poll document.readyState until interactive/complete.
            try
            {
                var timeout = TimeSpan.FromSeconds(20);
                var start = DateTime.UtcNow;
                var attempts = 0;
                while (!cts.IsCancellationRequested
                       && !_domReadyRaisedForCurrentNavigation
                       && DateTime.UtcNow - start < timeout)
                {
                    await Task.Delay(100, CancellationToken.None);
                    if (cts.IsCancellationRequested || _domReadyRaisedForCurrentNavigation) break;

                    attempts++;
                    var readyState = await InvokeScriptWithTimeoutAsync(ReadyStateScript, TimeSpan.FromSeconds(2));
                    var state = readyState?.Trim();
                    if (string.Equals(state, "interactive", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "complete", StringComparison.OrdinalIgnoreCase))
                    {
                        _domReadyTier = "readyState-poll";
                        _logger?.LogInfo($"DOMContentLoaded derived via readyState poll (attempt {attempts}, state='{state}').", Module);
                        RaiseDomContentLoaded(url);
                        break;
                    }
                }

                if (!_domReadyRaisedForCurrentNavigation && !cts.IsCancellationRequested)
                {
                    _logger?.LogWarning($"DOM-ready polling gave up after {attempts} attempts / 20s timeout for: {url}", Module);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"DOM-ready polling ended with: {ex.Message}", Module);
            }
        }

        private void StopDomReadyPolling()
        {
            _domReadyPollCts?.Cancel();
            _domReadyPollCts?.Dispose();
            _domReadyPollCts = null;
        }

        private void RaiseDomContentLoaded(string url)
        {
            if (_domReadyRaisedForCurrentNavigation) return;
            _domReadyRaisedForCurrentNavigation = true;
            _isDomReady = true;
            _logger?.LogInfo($"DOMContentLoaded fired (tier: {_domReadyTier ?? "unknown"}) for: {url}", Module);
            DomContentLoaded?.Invoke(this, url);
        }

        private async Task<string?> InvokeScriptWithTimeoutAsync(string script, TimeSpan timeout)
        {
            var task = _webView.InvokeScript(script);
            var completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task) return null;

            // Observe faults so abandoned/failed evaluations don't escalate to
            // UnobservedTaskException.
            _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            return task.Status == TaskStatus.RanToCompletion ? task.Result : null;
        }

        public string? Source
        {
            get => _webView.Source?.ToString();
            // NativeWebView.Source is a non-nullable Uri; a null assignment is
            // ignored (no consumer ever sets Source to null).
            set
            {
                _logger?.LogDebug($"Source set: {value}", Module);
                if (value != null) _webView.Source = new Uri(value);
            }
        }

        public bool CanGoBack => _webView.CanGoBack;
        public bool CanGoForward => _webView.CanGoForward;

        public void GoBack()
        {
            _logger?.LogInfo($"GoBack requested (CanGoBack={_webView.CanGoBack}).", Module);
            _webView.GoBack();
        }

        public void GoForward()
        {
            _logger?.LogInfo($"GoForward requested (CanGoForward={_webView.CanGoForward}).", Module);
            _webView.GoForward();
        }

        public void Reload()
        {
            _logger?.LogInfo($"Reload requested (Source={_webView.Source}).", Module);
            _webView.Refresh();
        }

        public void Navigate(string url)
        {
            _logger?.LogInfo($"Navigate requested: {url}", Module);
            _webView.Navigate(new Uri(url));
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            string? raw;
            try
            {
                raw = await _webView.InvokeScript(script);
            }
            catch (Exception ex)
            {
                // WebView2's ExecuteScriptAsync returns "null" when script
                // evaluation fails instead of throwing; match that.
                _logger?.LogWarning($"ExecuteScriptAsync failed: {ex.Message} (script: {Truncate(script, 120)})", Module);
                return "null";
            }

            // NativeWebView.InvokeScript returns the RAW JS string value
            // (jsc_value_to_string on WebKitGTK), whereas WebView2's
            // ExecuteScriptAsync returns the JSON representation of the result
            // (e.g. "\"result\""). Consumers (ModExtractorService.UnwrapJsonString)
            // JSON-deserialize the returned string, so serialize here to match
            // the Windows convention exactly.
            var result = raw == null ? "null" : JsonSerializer.Serialize(raw);
            _logger?.LogDebug($"ExecuteScriptAsync: script='{Truncate(script, 120)}' -> '{Truncate(result, 160)}'", Module);
            return result;
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        public event EventHandler<NavigationStartingEventArgs>? NavigationStarting;
        public event EventHandler<string>? NavigationCompleted;
        public event EventHandler<string>? DomContentLoaded;
        public event EventHandler<bool>? LoadingStateChanged;
    }
}
