using RimSharp.Handlers;
using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Collections.ObjectModel;

namespace RimSharp.ViewModels.Modules.Downloader
{
    public class DownloaderViewModel : ViewModelBase
    {
        private WebView2 _webView;
        private string _statusMessage;
        private ObservableCollection<DownloadItem> _downloadList;
        private bool _canGoBack;
        private bool _canGoForward;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<DownloadItem> DownloadList
        {
            get => _downloadList;
            set => SetProperty(ref _downloadList, value);
        }

        // Add properties with notification - we'll still keep these for informational purposes
        public bool CanGoBack
        {
            get => _canGoBack;
            private set => SetProperty(ref _canGoBack, value);
        }

        public bool CanGoForward
        {
            get => _canGoForward;
            private set => SetProperty(ref _canGoForward, value);
        }

        // Browser Navigation Commands - removing condition checks
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }

        // Other commands (stubs)
        public ICommand AddModCommand { get; }
        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand DownloadCommand { get; }

        public DownloaderViewModel()
        {
            // Initialize collections
            _downloadList = new ObservableCollection<DownloadItem>();

            // Initialize browser commands - modified to remove CanExecute conditions
            GoBackCommand = new RelayCommand(
                _ => ExecuteGoBackCommand(),
                null); // Removed condition so button is always enabled

            GoForwardCommand = new RelayCommand(
                _ => ExecuteGoForwardCommand(),
                null); // Removed condition so button is always enabled

            GoHomeCommand = new RelayCommand(
                ExecuteGoHome);

            // Initialize other commands as stubs
            AddModCommand = new RelayCommand(_ => { });
            SetupSteamCmdCommand = new RelayCommand(_ => { });
            CheckUpdatesCommand = new RelayCommand(_ => { });
            DownloadCommand = new RelayCommand(_ => { });
        }

        public void SetWebView(WebView2 webView)
        {
            _webView = webView;

            if (_webView != null)
            {
                // Subscribe to navigation events
                _webView.NavigationStarting += WebView_NavigationStarting;
                _webView.NavigationCompleted += WebView_NavigationCompleted;

                // Add a source changed event handler
                _webView.SourceChanged += (s, e) => UpdateNavigationState();

                // Initial state update
                UpdateNavigationState();
            }
        }

        private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // You might want to update status or perform other actions here
            StatusMessage = $"Loading: {e.Uri}";
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // Update navigation state after navigation completes
            UpdateNavigationState();
            StatusMessage = e.IsSuccess
                ? $"Loaded: {_webView.Source}"
                : $"Failed to load page: {e.WebErrorStatus}";
        }

        public void UpdateNavigationState()
        {
            if (_webView != null && _webView.CoreWebView2 != null)
            {
                // We'll still update these properties for informational purposes
                CanGoBack = _webView.CoreWebView2.CanGoBack;
                CanGoForward = _webView.CoreWebView2.CanGoForward;
            }
        }

        #region Browser Command Methods

        private void ExecuteGoBackCommand()
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                {
                    // We'll still check if we can go back before executing
                    // to prevent errors, but the button will always be enabled
                    if (_webView.CoreWebView2.CanGoBack)
                    {
                        _webView.CoreWebView2.GoBack();
                    }
                    UpdateNavigationState();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error going back: {ex.Message}";
            }
        }

        private void ExecuteGoForwardCommand()
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                {
                    // We'll still check if we can go forward before executing
                    // to prevent errors, but the button will always be enabled
                    if (_webView.CoreWebView2.CanGoForward)
                    {
                        _webView.CoreWebView2.GoForward();
                    }
                    UpdateNavigationState();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error going forward: {ex.Message}";
            }
        }

        private void ExecuteGoHome(object parameter)
        {
            try
            {
                _webView?.CoreWebView2?.Navigate("https://steamcommunity.com/app/294100/workshop/");
                UpdateNavigationState();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error navigating home: {ex.Message}";
            }
        }

        #endregion
    }

    // Supporting class for the DownloadList
    public class DownloadItem : ViewModelBase
    {
        private string _name;
        private string _url;
        private double _progress;
        private string _status;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }
    }
}