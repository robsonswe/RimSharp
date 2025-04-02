using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using RimSharp.ViewModels.Modules.Downloader; // Assuming this namespace is correct
using System.Threading.Tasks; // <-- Add this using directive
using System.Windows; // <-- Add this for Dispatcher

namespace RimSharp.Views.Modules.Downloader
{
    // Ensure this partial class declaration matches x:Class in XAML
    public partial class DownloaderView : UserControl
    {
        public DownloaderView()
        {
            // This call MUST be first in the constructor for a XAML-based control
            InitializeComponent(); // <-- This should now be recognized after rebuild

            // Call InitializeAsync using discard with async Task is correct for fire-and-forget
            _ = InitializeAsync();
        }

        // Change signature to return Task
        private async Task InitializeAsync()
        {
            // Ensure CoreWebView2Environment is available (good practice)
            CoreWebView2Environment environment = null;
            try
            {
                // Optionally specify user data folder, or null for default
                environment = await CoreWebView2Environment.CreateAsync(null, null, null);
            }
            catch (Exception envEx)
            {
                 Console.WriteLine($"WebView2 Environment creation failed: {envEx.Message}");
                 MessageBox.Show($"Failed to prepare the web browser environment: {envEx.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 return; // Cannot proceed
            }

            try
            {
                // Now ensure the control itself is initialized with the created environment
                // This method replaces EnsureCoreWebView2Async() when you create the environment manually
                await SteamWorkshopBrowser.EnsureCoreWebView2Async(environment); // <-- Should be recognized

                // Check if CoreWebView2 is actually available after awaiting
                if (SteamWorkshopBrowser.CoreWebView2 != null) // <-- Should be recognized
                {
                    // Set the WebView2 reference in the ViewModel ONLY after successful initialization
                    // Use Dispatcher for safety, although DataContext is likely set by now
                    await Dispatcher.InvokeAsync(() => // Use InvokeAsync with async Task
                    {
                        if (DataContext is DownloaderViewModel viewModel)
                        {
                            viewModel.SetWebView(SteamWorkshopBrowser); // <-- Should be recognized
                        }
                    });

                    // Configure WebView2 settings
                    SteamWorkshopBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    SteamWorkshopBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    SteamWorkshopBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;

                    // Initial navigation is usually handled by the Source property in XAML,
                    // but you could navigate programmatically here if needed AFTER initialization.
                    // SteamWorkshopBrowser.CoreWebView2.Navigate("https://steamcommunity.com/app/294100/workshop/");
                }
                else
                {
                    Console.WriteLine("WebView2 CoreWebView2 is null after EnsureCoreWebView2Async.");
                    MessageBox.Show("Failed to initialize the web browser component (CoreWebView2 is null).", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                // Handle WebView2 control initialization errors
                Console.WriteLine($"WebView2 control initialization failed: {ex.Message}");
                MessageBox.Show($"Failed to initialize the web browser component: {ex.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Optional DataContextChanged handler (if needed)
        // Remember to add DataContextChanged="DownloaderView_DataContextChanged" to UserControl in XAML
        // private void DownloaderView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        // {
        //     if (SteamWorkshopBrowser?.CoreWebView2 != null && e.NewValue is DownloaderViewModel viewModel)
        //     {
        //         viewModel.SetWebView(SteamWorkshopBrowser);
        //     }
        // }
    }
}