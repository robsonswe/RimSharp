using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using RimSharp.ViewModels.Modules.Downloader;
using System.Threading.Tasks;
using System.Windows;

namespace RimSharp.Views.Modules.Downloader
{
    public partial class DownloaderView : UserControl
    {
        public DownloaderView()
        {
            // This call MUST be first in the constructor for a XAML-based control
            InitializeComponent();

            // Call InitializeAsync using discard with async Task is correct for fire-and-forget
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Important: Do not create a new environment or call EnsureCoreWebView2Async
                // if the Source property is set in XAML

                // Instead, just wait until the control is fully loaded
                if (SteamWorkshopBrowser.CoreWebView2 == null)
                {
                    // Wait for the control to be initialized by WPF
                    await Task.Delay(100);

                    // If still not initialized, we'll try once manually but with default environment
                    if (SteamWorkshopBrowser.CoreWebView2 == null)
                    {
                        try
                        {
                            await SteamWorkshopBrowser.EnsureCoreWebView2Async();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"WebView2 initialization failed: {ex.Message}");
                            MessageBox.Show($"Failed to initialize the web browser: {ex.Message}",
                                "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }

                // Configure WebView2 settings once it's available
                if (SteamWorkshopBrowser.CoreWebView2 != null)
                {
                    // Set the WebView2 reference in the ViewModel
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (DataContext is DownloaderViewModel viewModel)
                        {
                            viewModel.SetWebView(SteamWorkshopBrowser);
                        }
                    });

                    // Configure WebView2 settings
                    SteamWorkshopBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    SteamWorkshopBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    SteamWorkshopBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;

                    // Initial navigation only if not already set via Source property
                    // Add after the initial navigation in InitializeAsync
                    if (SteamWorkshopBrowser.Source == null ||
    SteamWorkshopBrowser.Source.ToString() == "about:blank")
                    {
                        SteamWorkshopBrowser.CoreWebView2.Navigate("https://steamcommunity.com/app/294100/workshop/");

                        // Give some time for the navigation to complete
                        await Task.Delay(500);

                        // Update the navigation state
                        if (DataContext is DownloaderViewModel viewModel)
                        {
                            viewModel.UpdateNavigationState();
                        }
                    }
                }
                else
                {
                    Console.WriteLine("WebView2 CoreWebView2 is null after initialization attempts.");
                    MessageBox.Show("Failed to initialize the web browser component.",
                        "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebView2 initialization failed: {ex.Message}");
                MessageBox.Show($"Failed to initialize the web browser: {ex.Message}",
                    "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SteamWorkshopBrowser_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (DataContext is DownloaderViewModel viewModel)
            {
                viewModel.UpdateNavigationState();
            }
        }
    }
}