// Features/WorkshopDownloader/Components/Browser/LinuxBrowserFactory.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    /// <summary>
    /// Linux implementation of <see cref="IBrowserFactory"/> creating a
    /// <see cref="LinuxNativeWebView"/> (backend selection: WPE WebKit
    /// preferred, WebKitGTK 4.1 fallback - forced explicitly via
    /// AdapterPreference instead of relying on the package default order).
    /// </summary>
    public class LinuxBrowserFactory : IBrowserFactory
    {
        private const string Module = "LinuxBrowserFactory";

        // Static so the probe performed at DI-registration time (with a null
        // logger) is reused by the real factory instance resolved later.
        private static bool? _isSupportedCache;

        private readonly ILoggerService? _logger;

        public LinuxBrowserFactory(ILoggerService? logger = null)
        {
            _logger = logger;
        }

        public bool IsSupported => _isSupportedCache ??= ProbeSupported();

        private bool ProbeSupported()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _logger?.LogDebug("Not Linux - not supported.", Module);
                return false;
            }

            var gdkBackend = Environment.GetEnvironmentVariable("GDK_BACKEND");
            var gtkVersion = Environment.GetEnvironmentVariable("GTK_VERSION");
            _logger?.LogInfo($"Capability probe starting. GDK_BACKEND='{gdkBackend ?? "<unset>"}', GTK_VERSION='{gtkVersion ?? "<unset>"}', DISPLAY='{Environment.GetEnvironmentVariable("DISPLAY") ?? "<unset>"}', WAYLAND_DISPLAY='{Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "<unset>"}'", Module);

            // Preferred: the package's own runtime capability probe.
            try
            {
                var gtk = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk);
                _logger?.LogInfo($"WebKitGTK probe: supported={gtk.IsSupported}, installed={gtk.IsInstalled}, engine={gtk.Engine}, version={gtk.Version}, scenario={gtk.SupportedScenarios}, reason='{gtk.UnavailableReason}'", Module);
                if (gtk.IsSupported && gtk.IsInstalled) return true;

                var wpe = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WpeWebKit);
                _logger?.LogInfo($"WPE probe: supported={wpe.IsSupported}, installed={wpe.IsInstalled}, engine={wpe.Engine}, version={wpe.Version}, scenario={wpe.SupportedScenarios}, reason='{wpe.UnavailableReason}'", Module);
                if (wpe.IsSupported && wpe.IsInstalled) return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"Adapter probe failed, falling back to native library probe: {ex.Message}", Module);
            }

            // Fallback: try to load the WebKitGTK 4.1 library directly.
            try
            {
                var ok = NativeLibrary.TryLoad("libwebkit2gtk-4.1.so.0", out IntPtr _);
                _logger?.LogInfo($"Native library probe libwebkit2gtk-4.1.so.0: {ok}", Module);
                return ok;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Native library probe failed: {ex.Message}", Module);
                return false;
            }
        }

        public Task<(Control View, IBrowserControl Controller)> CreateBrowserAsync()
        {
            if (!IsSupported)
            {
                throw new PlatformNotSupportedException(
                    "The embedded web browser requires WebKitGTK 4.1 (libwebkit2gtk-4.1-0) or WPE WebKit (libwpewebkit-2.0) on Linux.");
            }

            string defaultUrl = "https://steamcommunity.com/app/294100/workshop/";
            _logger?.LogInfo($"Creating LinuxNativeWebView, defaultUrl={defaultUrl}", Module);

            // LinuxNativeWebView is itself an Avalonia Control (it hosts the
            // native WebKit view via NativeControlHost, or renders offscreen
            // through the compositor for WPE), so it is returned directly as
            // the View - no extra host wrapper is needed, unlike the WinForms
            // WebView2 on Windows.
            var webView = new LinuxNativeWebView();

            // Force backend selection: WPE first (offscreen compositor path,
            // sidesteps the X11 NativeControlHost embedding entirely), WebKitGTK
            // as fallback. The package default order is unknown/unstable here.
            webView.AdapterPreference = new List<WebViewAdapterType>
            {
                WebViewAdapterType.WpeWebKit,
                WebViewAdapterType.WebKitGtk
            };
            _logger?.LogInfo("AdapterPreference set to [WpeWebKit, WebKitGtk].", Module);

            webView.EnvironmentRequested += OnEnvironmentRequested;
            webView.AdapterCreated += OnAdapterCreated;
            webView.AdapterDestroyed += OnAdapterDestroyed;

            webView.Source = new Uri(defaultUrl);
            _logger?.LogInfo($"Source set to {defaultUrl} (navigation starts once the control is attached to the visual tree).", Module);

            var controller = new LinuxBrowserControl(webView, _logger);

            (Control View, IBrowserControl Controller) result = (webView, controller);
            return Task.FromResult(result);
        }

        private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
        {
            _logger?.LogInfo($"EnvironmentRequested: {e.GetType().Name}", Module);
            switch (e)
            {
                case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                    // SHM rendering is the most reliable path inside VMs
                    // (EGL/DmaBuf allocation commonly fails or renders blank
                    // on virtual GPUs).
                    wpe.RenderingMode = WpeRenderingMode.Shm;
                    _logger?.LogInfo("WPE environment configured: RenderingMode=Shm.", Module);
                    break;

                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    // The app runs on X11 (XFCE/xfwm4); force the X11 GDK
                    // backend so the adapter's GTK init cannot pick Wayland.
                    gtk.ForceX11GdkBackend = true;
                    _logger?.LogInfo("GTK environment configured: ForceX11GdkBackend=true.", Module);
                    break;
            }
        }

        private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
        {
            if (sender is NativeWebView wv)
            {
                var info = wv.AdapterInfo;
                _logger?.LogInfo($"AdapterCreated: type={info?.Type}, engine={info?.Engine}, version={info?.Version}", Module);
            }
            else
            {
                _logger?.LogInfo($"AdapterCreated (sender: {sender?.GetType().Name ?? "null"}).", Module);
            }
        }

        private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs e)
        {
            _logger?.LogInfo("AdapterDestroyed.", Module);
        }
    }
}
