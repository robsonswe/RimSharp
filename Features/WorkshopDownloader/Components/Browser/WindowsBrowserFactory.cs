// Features/WorkshopDownloader/Components/Browser/WindowsBrowserFactory.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class WebView2Host : NativeControlHost
    {
        private readonly WebView2 _webView;
        private IPlatformHandle? _currentHandle;

        public WebView2Host(WebView2 webView)
        {
            _webView = webView;
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_currentHandle != null)
                {
                    return _currentHandle;
                }
                if (_webView.Handle == IntPtr.Zero)
                {
                    _webView.CreateControl();
                }

                _currentHandle = new PlatformHandle(_webView.Handle, "HWND");
                return _currentHandle;
            }
            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _currentHandle = null;
        }
    }

    public class WindowsBrowserFactory : IBrowserFactory
    {
        public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public async Task<(Control View, IBrowserControl Controller)> CreateBrowserAsync()
        {
            if (!IsSupported)
            {
                throw new PlatformNotSupportedException("WebView2 is only supported on Windows.");
            }

            string defaultUrl = "https://steamcommunity.com/app/294100/workshop/";

            var webView = new WebView2
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Visible = true,
                Source = new Uri(defaultUrl)
            };

            Debug.WriteLine("[WindowsBrowserFactory] Calling EnsureCoreWebView2Async...");
            await webView.EnsureCoreWebView2Async();
            Debug.WriteLine("[WindowsBrowserFactory] EnsureCoreWebView2Async completed.");

            if (webView.CoreWebView2 != null)
            {
                Debug.WriteLine("[WindowsBrowserFactory] Configuring CoreWebView2 settings...");
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            }
            else
            {
                throw new InvalidOperationException("CoreWebView2 is null after initialization.");
            }

            var controller = new WindowsBrowserControl(webView);
            var host = new WebView2Host(webView);

            return (host, controller);
        }
    }
}
