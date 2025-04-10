using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using System.Windows;
using RimSharp.Features.WorkshopDownloader.Components.Browser;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public partial class BrowserView : UserControl
    {
        public BrowserView()
        {
            InitializeComponent();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (WebBrowser.CoreWebView2 == null)
                {
                    try
                    {
                        await WebBrowser.EnsureCoreWebView2Async();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WebView2 initialization failed: {ex.Message}");
                        MessageBox.Show($"Failed to initialize the web browser: {ex.Message}",
                            "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (WebBrowser.CoreWebView2 != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (DataContext is BrowserViewModel viewModel)
                        {
                            viewModel.SetWebView(WebBrowser);
                        }
                        else
                        {
                            Console.WriteLine("DataContext not yet available when trying to SetWebView.");
                        }
                    });

                    // Configure WebView2 settings
                    WebBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    WebBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    WebBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;

                    if (WebBrowser.Source == null || WebBrowser.Source.ToString() == "about:blank")
                    {
                        WebBrowser.CoreWebView2.Navigate("https://steamcommunity.com/app/294100/workshop/");
                    }
                }
                else
                {
                    Console.WriteLine("WebView2 CoreWebView2 is unexpectedly null after initialization attempts.");
                    MessageBox.Show("Failed to initialize the web browser component.",
                        "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebView2 setup failed: {ex.Message}");
                MessageBox.Show($"Failed to set up the web browser: {ex.Message}",
                    "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}