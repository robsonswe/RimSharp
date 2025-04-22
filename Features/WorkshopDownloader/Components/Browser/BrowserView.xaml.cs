using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using System.Windows;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using System.Diagnostics; // Added for Debug

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public partial class BrowserView : UserControl
    {
        public BrowserView()
        {
            InitializeComponent();
            // Ensure DataContext is set before calling InitializeAsync if possible,
            // or handle DataContextChanged event.
            this.DataContextChanged += BrowserView_DataContextChanged;
            // Initialize WebView2 eagerly
             _ = InitializeAsync(); // Fire and forget is okay here
        }

         // Handle DataContext change to ensure ViewModel gets the WebView reference
         // even if DataContext is set after InitializeAsync starts
         private void BrowserView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
         {
             Debug.WriteLine("[BrowserView] DataContextChanged fired.");
             if (DataContext is BrowserViewModel viewModel && WebBrowser != null && WebBrowser.CoreWebView2 != null)
             {
                 Debug.WriteLine("[BrowserView] DataContext is BrowserViewModel, attempting SetWebView.");
                 viewModel.SetWebView(WebBrowser);
             }
              else if (DataContext is BrowserViewModel) {
                 Debug.WriteLine("[BrowserView] DataContext is BrowserViewModel, but WebView/CoreWebView2 not ready yet.");
             }
         }

        private async Task InitializeAsync()
        {
             Debug.WriteLine("[BrowserView] InitializeAsync started.");
            try
            {
                // Ensure CoreWebView2 is initialized. The Source property in XAML
                // often triggers this, but calling it explicitly is safer.
                Debug.WriteLine("[BrowserView] Calling EnsureCoreWebView2Async...");
                await WebBrowser.EnsureCoreWebView2Async();
                Debug.WriteLine("[BrowserView] EnsureCoreWebView2Async completed.");

                // CoreWebView2 should be available now if successful
                 if (WebBrowser.CoreWebView2 != null)
                 {
                    Debug.WriteLine("[BrowserView] CoreWebView2 is available. Configuring settings.");
                     // Configure WebView2 settings AFTER initialization
                     WebBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                     WebBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                     WebBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false; // Usually hidden by default

                     // Try setting the WebView on the ViewModel immediately
                     // It's okay if DataContext isn't ready yet, DataContextChanged will handle it.
                     if (DataContext is BrowserViewModel viewModel)
                     {
                        Debug.WriteLine("[BrowserView] DataContext available, calling SetWebView.");
                         viewModel.SetWebView(WebBrowser);
                     }
                     else
                     {
                         Debug.WriteLine("[BrowserView] DataContext not yet available after EnsureCoreWebView2Async.");
                     }

                     // Initial navigation is now handled by the Source property in XAML
                     // No need for: WebBrowser.CoreWebView2.Navigate(...) here
                 }
                 else
                 {
                     Debug.WriteLine("[BrowserView] CoreWebView2 is unexpectedly null after EnsureCoreWebView2Async.");
                     MessageBox.Show("Failed to initialize the web browser component.",
                         "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 }
            }
             // Catch specific WebView2 exceptions if needed
             catch (WebView2RuntimeNotFoundException ex)
             {
                  Debug.WriteLine($"[BrowserView] WebView2 Runtime not found: {ex.Message}");
                  MessageBox.Show($"The required WebView2 Runtime is not installed. Please install it and restart the application.\n\nDetails: {ex.Message}",
                      "Missing Dependency", MessageBoxButton.OK, MessageBoxImage.Error);
             }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserView] WebView2 initialization failed: {ex.Message}");
                // Avoid showing message box if it's a common cancellation during shutdown etc.
                if (!(ex is OperationCanceledException)) {
                     MessageBox.Show($"Failed to initialize the web browser: {ex.Message}",
                         "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
             Debug.WriteLine("[BrowserView] InitializeAsync finished.");
        }
    }
}
