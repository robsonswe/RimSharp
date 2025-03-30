using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using RimSharp.ViewModels.Modules.Downloader;

namespace RimSharp.Views.Modules.Downloader
{
    public partial class DownloaderView : UserControl
    {
        public DownloaderView()
        {
            InitializeComponent();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                // Ensure the WebView2 runtime is installed and initialize the control
                await SteamWorkshopBrowser.EnsureCoreWebView2Async();
                
                // Set the WebView2 reference in the ViewModel
                if (DataContext is DownloaderViewModel viewModel)
                {
                    viewModel.SetWebView(SteamWorkshopBrowser);
                }
                
                // Optional: Configure WebView2 settings
                SteamWorkshopBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                SteamWorkshopBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                SteamWorkshopBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            }
            catch (Exception ex)
            {
                // Handle WebView2 initialization errors
                Console.WriteLine($"WebView2 initialization failed: {ex.Message}");
            }
        }
    }
}