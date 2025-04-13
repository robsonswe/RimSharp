#nullable enable
using System;
using System.ComponentModel; // Added for PropertyChangedEventHandler, PropertyChangedEventArgs
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using RimSharp.Core.Commands.Base; // Added for DelegateCommand
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Threading.Tasks;
using System.Threading;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class BrowserViewModel : ViewModelBase, IDisposable
    {
        private readonly IWebNavigationService _navigationService;
        private readonly DownloaderViewModel _parentViewModel;
        private WebView2? _webView;
        private IModExtractorService? _extractorService;
        private bool _isDisposed = false;

        // Properties (synced with navigation service)
        private bool _canGoBack;
        public bool CanGoBack
        {
            get => _canGoBack;
            private set => SetProperty(ref _canGoBack, value);
        }

        private bool _canGoForward;
        public bool CanGoForward
        {
            get => _canGoForward;
            private set => SetProperty(ref _canGoForward, value);
        }

        private bool _isValidModUrl;
        public bool IsValidModUrl
        {
            get => _isValidModUrl;
            private set => SetProperty(ref _isValidModUrl, value);
        }

        private string _currentUrl = string.Empty;
        public string CurrentUrl
        {
            get => _currentUrl;
            private set => SetProperty(ref _currentUrl, value);
        }

        private bool _isModInfoAvailable;
        public bool IsModInfoAvailable
        {
            get => _isModInfoAvailable;
            private set => SetProperty(ref _isModInfoAvailable, value);
        }

        private bool _isOperationInProgress;
        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            private set => SetProperty(ref _isOperationInProgress, value);
        }

        // Commands
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand NavigateToUrlCommand { get; }

        // Events
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ModInfoAvailabilityChanged;

        // Named handlers for unsubscribing
        private EventHandler<string>? _navStatusHandler;
        private EventHandler? _navStateChangedHandler;
        private EventHandler<bool>? _navModUrlValidityHandler;
        private EventHandler<string>? _navSucceededAndValidHandler;
        private EventHandler? _extractorModInfoAvailableHandler;
        private PropertyChangedEventHandler? _parentPropertyChangedHandler;

        public BrowserViewModel(IWebNavigationService navigationService, DownloaderViewModel parentViewModel)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _parentViewModel = parentViewModel ?? throw new ArgumentNullException(nameof(parentViewModel));

            // Initialize properties from service/parent
            _canGoBack = _navigationService.CanGoBack;
            _canGoForward = _navigationService.CanGoForward;
            _isValidModUrl = _navigationService.IsValidModUrl;
            _currentUrl = _navigationService.CurrentUrl;
            _isOperationInProgress = _parentViewModel.IsOperationInProgress;

            // Subscribe to navigation service events
            _navStatusHandler = NavigationService_StatusChanged;
            _navigationService.StatusChanged += _navStatusHandler;

            _navStateChangedHandler = NavigationService_NavigationStateChanged;
            _navigationService.NavigationStateChanged += _navStateChangedHandler;

            _navModUrlValidityHandler = NavigationService_ModUrlValidityChanged;
            _navigationService.ModUrlValidityChanged += _navModUrlValidityHandler;

            _navSucceededAndValidHandler = NavigationService_NavigationSucceededAndUrlValid;
            _navigationService.NavigationSucceededAndUrlValid += _navSucceededAndValidHandler;

            // Subscribe to parent VM changes
            _parentPropertyChangedHandler = ParentViewModel_PropertyChanged;
            _parentViewModel.PropertyChanged += _parentPropertyChangedHandler;

            // Initialize commands using base class helpers
            GoBackCommand = CreateCommand(
                () => _navigationService.GoBack(),
                () => !IsOperationInProgress && CanGoBack,
                nameof(IsOperationInProgress), nameof(CanGoBack)
            );

            GoForwardCommand = CreateCommand(
                () => _navigationService.GoForward(),
                () => !IsOperationInProgress && CanGoForward,
                nameof(IsOperationInProgress), nameof(CanGoForward)
            );

            GoHomeCommand = CreateCommand(
                () => _navigationService.GoHome(),
                () => !IsOperationInProgress,
                nameof(IsOperationInProgress)
            );

            NavigateToUrlCommand = CreateCommand<string>(
                ExecuteNavigateToUrlCommand,
                url => !IsOperationInProgress,
                nameof(IsOperationInProgress)
            );
        }

        public void SetWebView(WebView2 webView)
        {
            if (_isDisposed) return;
            _webView = webView;
            _navigationService.SetWebView(webView);

            if (_extractorService != null)
            {
                _extractorService.IsModInfoAvailableChanged -= _extractorModInfoAvailableHandler;
            }

            _extractorService = new ModExtractorService(webView);
            _extractorModInfoAvailableHandler = ExtractorService_IsModInfoAvailableChanged;
            _extractorService.IsModInfoAvailableChanged += _extractorModInfoAvailableHandler;

            RunOnUIThread(() =>
            {
                IsModInfoAvailable = _extractorService.IsModInfoAvailable;
                ModInfoAvailabilityChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public Task<ModInfoDto?> GetCurrentModInfoAsync(CancellationToken token = default)
        {
            if (_isDisposed || _extractorService == null)
            {
                StatusChanged?.Invoke(this, "Mod extractor not initialized or VM disposed.");
                return Task.FromResult<ModInfoDto?>(null);
            }
            try
            {
                return _extractorService.ExtractFullModInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting mod info: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error extracting mod info: {ex.Message}");
                return Task.FromResult<ModInfoDto?>(null);
            }
        }

        // Event Handlers
        private void ParentViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DownloaderViewModel.IsOperationInProgress))
            {
                RunOnUIThread(() => IsOperationInProgress = _parentViewModel.IsOperationInProgress);
            }
        }

        private void NavigationService_StatusChanged(object? sender, string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        private void NavigationService_NavigationStateChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() =>
            {
                CanGoBack = _navigationService.CanGoBack;
                CanGoForward = _navigationService.CanGoForward;
                CurrentUrl = _navigationService.CurrentUrl;
            });
        }

        private void NavigationService_ModUrlValidityChanged(object? sender, bool isValid)
        {
            RunOnUIThread(() => IsValidModUrl = isValid);
        }

        private async void NavigationService_NavigationSucceededAndUrlValid(object? sender, string url)
        {
            if (_isDisposed || _extractorService == null) return;

            Debug.WriteLine($"[BrowserVM] Navigated to valid URL ({url}). Triggering info extraction.");
            try
            {
                await _extractorService.ExtractFullModInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Extraction failed: {ex.Message}");
                RunOnUIThread(() => StatusChanged?.Invoke(this, $"Error during info extraction: {ex.Message}"));
            }
        }

        private void ExtractorService_IsModInfoAvailableChanged(object? sender, EventArgs e)
        {
            if (_isDisposed || _extractorService == null) return;

            RunOnUIThread(() =>
            {
                bool newValue = _extractorService.IsModInfoAvailable;
                if (IsModInfoAvailable != newValue)
                {
                    IsModInfoAvailable = newValue;
                    Debug.WriteLine($"[BrowserVM] ExtractorService_IsModInfoAvailableChanged. IsModInfoAvailable: {IsModInfoAvailable}");
                    ModInfoAvailabilityChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        // Command Logic
        private void ExecuteNavigateToUrlCommand(string urlString)
        {
            if (string.IsNullOrEmpty(urlString)) return;

            try
            {
                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Navigate(urlString);
                }
                else if (_webView != null && Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
                {
                    _webView.Source = uri;
                    Debug.WriteLine("[BrowserVM] Warning: Navigating via Source property.");
                }
                else
                {
                    Debug.WriteLine("[BrowserVM] WebView or CoreWebView2 not initialized.");
                    StatusChanged?.Invoke(this, "Browser component not ready.");
                }
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"[BrowserVM] Invalid URL format: {urlString} - {ex.Message}");
                StatusChanged?.Invoke(this, $"Invalid URL: {urlString}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Navigation error: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error navigating to URL: {ex.Message}");
            }
        }

        // Dispose Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                Debug.WriteLine("[BrowserVM] Disposing...");
                // Unsubscribe from navigation service
                if (_navigationService != null)
                {
                    if (_navStatusHandler != null) _navigationService.StatusChanged -= _navStatusHandler;
                    if (_navStateChangedHandler != null) _navigationService.NavigationStateChanged -= _navStateChangedHandler;
                    if (_navModUrlValidityHandler != null) _navigationService.ModUrlValidityChanged -= _navModUrlValidityHandler;
                    if (_navSucceededAndValidHandler != null) _navigationService.NavigationSucceededAndUrlValid -= _navSucceededAndValidHandler;
                }

                // Unsubscribe from extractor service
                if (_extractorService != null)
                {
                    if (_extractorModInfoAvailableHandler != null) _extractorService.IsModInfoAvailableChanged -= _extractorModInfoAvailableHandler;
                    (_extractorService as IDisposable)?.Dispose();
                }

                // Unsubscribe from parent VM
                if (_parentViewModel != null && _parentPropertyChangedHandler != null)
                {
                    _parentViewModel.PropertyChanged -= _parentPropertyChangedHandler;
                }

                // Clear handlers
                _navStatusHandler = null;
                _navStateChangedHandler = null;
                _navModUrlValidityHandler = null;
                _navSucceededAndValidHandler = null;
                _extractorModInfoAvailableHandler = null;
                _parentPropertyChangedHandler = null;

                _webView = null;
                Debug.WriteLine("[BrowserVM] Dispose complete.");
            }

            _isDisposed = true;
        }

        public void Cleanup()
        {
            Dispose();
        }

        ~BrowserViewModel()
        {
            Dispose(false);
        }
    }
}