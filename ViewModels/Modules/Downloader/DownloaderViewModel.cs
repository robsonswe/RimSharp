using RimSharp.Handlers;
using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace RimSharp.ViewModels.Modules.Downloader
{
    public class DownloaderViewModel : ViewModelBase
    {
        private WebView2 _webView;
        private string _statusMessage;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // Browser Navigation Commands
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
            // Initialize browser commands
            GoBackCommand = new RelayCommand(
                ExecuteGoBack,
                CanExecuteGoBack);
            
            GoForwardCommand = new RelayCommand(
                ExecuteGoForward,
                CanExecuteGoForward);
            
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
                _webView.NavigationCompleted += (s, e) => 
                {
                    OnPropertyChanged(nameof(CanExecuteGoBack));
                    OnPropertyChanged(nameof(CanExecuteGoForward));
                    StatusMessage = $"Loaded: {_webView.Source}";
                };
            }
        }

        #region Browser Command Methods

        private void ExecuteGoBack(object parameter)
        {
            try
            {
                _webView?.GoBack();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error going back: {ex.Message}";
            }
        }

        private bool CanExecuteGoBack(object parameter) => _webView?.CanGoBack ?? false;

        private void ExecuteGoForward(object parameter)
        {
            try
            {
                _webView?.GoForward();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error going forward: {ex.Message}";
            }
        }

        private bool CanExecuteGoForward(object parameter) => _webView?.CanGoForward ?? false;

        private void ExecuteGoHome(object parameter)
        {
            try
            {
                _webView?.CoreWebView2?.Navigate("https://steamcommunity.com/app/294100/workshop/");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error navigating home: {ex.Message}";
            }
        }

        #endregion
    }
}