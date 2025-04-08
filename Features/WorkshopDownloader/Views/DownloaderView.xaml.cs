using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using System.Windows;
using RimSharp.Features.WorkshopDownloader.ViewModels;

namespace RimSharp.Features.WorkshopDownloader.Views
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
                // Check if the CoreWebView2 is already initialized (e.g., by setting Source in XAML)
                // If not, EnsureCoreWebView2Async might be needed.
                if (SteamWorkshopBrowser.CoreWebView2 == null)
                {
                     // It's generally better to wait for CoreWebView2InitializationCompleted event
                     // but EnsureCoreWebView2Async is often used here.
                    try
                    {
                        // Ensure initialization. If Source is set in XAML, this might wait
                        // for the initial navigation implicitly triggered by Source.
                        // If Source is not set, it initializes with about:blank or specified environment.
                        await SteamWorkshopBrowser.EnsureCoreWebView2Async();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WebView2 initialization failed: {ex.Message}");
                        MessageBox.Show($"Failed to initialize the web browser: {ex.Message}",
                            "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return; // Stop further execution if initialization fails
                    }
                }


                // At this point, CoreWebView2 should be available (either from XAML source or EnsureCoreWebView2Async)
                if (SteamWorkshopBrowser.CoreWebView2 != null)
                {
                    // Set the WebView2 reference in the ViewModel
                    // Use Dispatcher if necessary, though often DataContext is set before this runs
                    // Ensure DataContext is actually set. Consider checking for null or using Loaded event.
                    await Dispatcher.InvokeAsync(() => // Ensure running on UI thread if needed
                    {
                        if (DataContext is DownloaderViewModel viewModel)
                        {
                            viewModel.SetWebView(SteamWorkshopBrowser); // Pass the control to the VM/Service
                        }
                        else
                        {
                             // Consider logging or handling the case where DataContext is not yet set
                             Console.WriteLine("DataContext not yet available when trying to SetWebView.");
                        }
                    });


                    // Configure WebView2 settings
                    SteamWorkshopBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    SteamWorkshopBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true; // Keep default menus for now
                    SteamWorkshopBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;

                    // Perform initial navigation ONLY if Source wasn't set in XAML
                    // Check the actual current Source after initialization
                    if (SteamWorkshopBrowser.Source == null || SteamWorkshopBrowser.Source.ToString() == "about:blank")
                    {
                        // Let the Navigation Service handle navigation if possible,
                        // otherwise navigate directly. The service should detect this navigation.
                        SteamWorkshopBrowser.CoreWebView2.Navigate("https://steamcommunity.com/app/294100/workshop/");

                        // NO NEED to call UpdateNavigationState here. The service should react
                        // to NavigationCompleted or SourceChanged events from the WebView.
                    }
                     // If Source *was* set in XAML, the navigation is already in progress or completed.
                     // The IWebNavigationService should have already detected this via its event handlers
                     // hooked up during/after SetWebView.
                }
                else
                {
                    // This case should ideally not be reached if EnsureCoreWebView2Async succeeded or Source was set.
                    Console.WriteLine("WebView2 CoreWebView2 is unexpectedly null after initialization attempts.");
                    MessageBox.Show("Failed to initialize the web browser component.",
                        "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) // Catch any broader exceptions during the async process
            {
                Console.WriteLine($"WebView2 setup failed: {ex.Message}");
                MessageBox.Show($"Failed to set up the web browser: {ex.Message}",
                    "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}