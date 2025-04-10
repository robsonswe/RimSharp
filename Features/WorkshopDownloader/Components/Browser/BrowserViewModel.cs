#nullable enable
using System;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.ViewModels; // Keep reference to parent for IsOperationInProgress
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models; // Needed for ModInfoDto
using System.Threading.Tasks; // Needed for Task
using System.Threading; // Needed for CancellationToken

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class BrowserViewModel : ViewModelBase
    {
        private readonly IWebNavigationService _navigationService;
        private readonly DownloaderViewModel _parentViewModel; // Keep parent for IsOperationInProgress checks
        private WebView2? _webView;
        private IModExtractorService? _extractorService; // Add extractor service field

        // Properties
        public bool CanGoBack => _navigationService.CanGoBack;
        public bool CanGoForward => _navigationService.CanGoForward;
        public bool IsValidModUrl => _navigationService.IsValidModUrl;
        public string CurrentUrl => _navigationService.CurrentUrl;

        // NEW Property: Expose mod info availability
        public bool IsModInfoAvailable => _extractorService?.IsModInfoAvailable ?? false;

        // Commands
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand NavigateToUrlCommand { get; }

        // Events
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ModInfoAvailabilityChanged; // NEW: Event for DownloadQueueViewModel

        public BrowserViewModel(IWebNavigationService navigationService, DownloaderViewModel parentViewModel)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _parentViewModel = parentViewModel ?? throw new ArgumentNullException(nameof(parentViewModel)); // Store parent

            // Subscribe to navigation service events
            _navigationService.StatusChanged += NavigationService_StatusChanged;
            _navigationService.NavigationStateChanged += NavigationService_NavigationStateChanged;
            _navigationService.ModUrlValidityChanged += NavigationService_ModUrlValidityChanged;
            _navigationService.NavigationSucceededAndUrlValid += NavigationService_NavigationSucceededAndUrlValid; // Subscribe to this

            // Initialize commands (reference parent's IsOperationInProgress)
            GoBackCommand = new RelayCommand(
                _ => _navigationService.GoBack(),
                _ => CanGoBack && !_parentViewModel.IsOperationInProgress);

            GoForwardCommand = new RelayCommand(
                _ => _navigationService.GoForward(),
                _ => CanGoForward && !_parentViewModel.IsOperationInProgress);

            GoHomeCommand = new RelayCommand(
                _ => _navigationService.GoHome(),
                _ => !_parentViewModel.IsOperationInProgress);

            NavigateToUrlCommand = new RelayCommand(
                ExecuteNavigateToUrlCommand,
                _ => !_parentViewModel.IsOperationInProgress);
        }

        public void SetWebView(WebView2 webView)
        {
            _webView = webView;
            _navigationService.SetWebView(webView);

            // --- Extractor Service Initialization ---
            // Unsubscribe from previous if exists
            if (_extractorService != null)
            {
                _extractorService.IsModInfoAvailableChanged -= ExtractorService_IsModInfoAvailableChanged;
            }

            // Create and store the new extractor service instance
            _extractorService = new ModExtractorService(webView);
            // Subscribe to its event
            _extractorService.IsModInfoAvailableChanged += ExtractorService_IsModInfoAvailableChanged;

            // Update initial state
            RunOnUIThread(() =>
             {
                 OnPropertyChanged(nameof(IsModInfoAvailable));
                 ModInfoAvailabilityChanged?.Invoke(this, EventArgs.Empty);
                 // Commands depending on this don't live here, so no need to RaiseCanExecuteChanged
             });
            // --- End Extractor Service Initialization ---
        }

        // NEW Method: Allow getting mod info (called by DownloadQueueViewModel)
        public Task<ModInfoDto?> GetCurrentModInfoAsync(CancellationToken token = default) // Optional token
        {
            if (_extractorService == null)
            {
                StatusChanged?.Invoke(this, "Mod extractor not initialized.");
                return Task.FromResult<ModInfoDto?>(null);
            }
            try
            {
                // Pass token if the service supports it
                // return _extractorService.ExtractFullModInfo(token);
                return _extractorService.ExtractFullModInfo(); // Assuming ExtractFullModInfo doesn't need token yet
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting mod info: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error extracting mod info: {ex.Message}");
                return Task.FromResult<ModInfoDto?>(null);
            }
        }


        // --- Event Handlers ---

        private void NavigationService_StatusChanged(object? sender, string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        private void NavigationService_NavigationStateChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() => { // Ensure UI thread
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
                OnPropertyChanged(nameof(CurrentUrl));
                RefreshCommandStates();
            });
        }

        private void NavigationService_ModUrlValidityChanged(object? sender, bool isValid)
        {
             RunOnUIThread(() => { // Ensure UI thread
                OnPropertyChanged(nameof(IsValidModUrl));
                // State change might affect parent commands, parent should handle refreshing if needed
            });
        }

        // NEW Handler: Trigger extraction when navigating to a valid URL
        // *** CHANGED HERE: Removed Task.Run ***
        private async void NavigationService_NavigationSucceededAndUrlValid(object? sender, string url)
        {
            if (_extractorService != null)
            {
                Debug.WriteLine($"[BrowserVM] Navigated to valid URL ({url}). Triggering info extraction on UI thread.");
                try
                {
                    // Call extraction directly; await ensures it completes before continuing the handler (if needed)
                    // This runs on the UI thread because the event handler is invoked on the UI thread.
                    await _extractorService.ExtractFullModInfo();
                    // The result/state change will trigger ExtractorService_IsModInfoAvailableChanged
                }
                catch (Exception ex)
                {
                    // This catch is now likely for errors within ExtractFullModInfo itself
                    Debug.WriteLine($"[BrowserVM] Extraction failed: {ex.Message}");
                    RunOnUIThread(() => StatusChanged?.Invoke(this, $"Error during info extraction: {ex.Message}"));
                }
            }
        }


        // NEW Handler: Respond to changes from the extractor service
        private void ExtractorService_IsModInfoAvailableChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() => // Ensure UI thread for property/event changes
            {
                Debug.WriteLine($"[BrowserVM] ExtractorService_IsModInfoAvailableChanged. IsModInfoAvailable: {IsModInfoAvailable}");
                OnPropertyChanged(nameof(IsModInfoAvailable));
                ModInfoAvailabilityChanged?.Invoke(this, EventArgs.Empty); // Notify listeners (like DownloadQueueViewModel)
            });
        }

        // --- Command Logic ---

        // Can be called by Parent VM to reflect changes in IsOperationInProgress
        public void RefreshCommandStates()
        {
            RunOnUIThread(() =>
            {
                ((RelayCommand)GoBackCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GoForwardCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GoHomeCommand).RaiseCanExecuteChanged();
                ((RelayCommand)NavigateToUrlCommand).RaiseCanExecuteChanged();
            });
        }

        private void ExecuteNavigateToUrlCommand(object? url)
        {
            if (_parentViewModel.IsOperationInProgress) return; // Check parent state

            if (url is string urlString && !string.IsNullOrEmpty(urlString))
            {
                try
                {
                    // Ensure CoreWebView2 is available before navigating
                    if (_webView?.CoreWebView2 != null)
                    {
                        // Prefer CoreWebView2.Navigate for better control and event handling
                         _webView.CoreWebView2.Navigate(urlString);
                    }
                    else if (_webView != null && Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
                    {
                        // Fallback: Set source directly - might not trigger all navigation events reliably
                        _webView.Source = uri;
                        Debug.WriteLine("[BrowserVM] Warning: Navigating via Source property. CoreWebView2 might not be fully ready.");
                    }
                    else
                    {
                        Debug.WriteLine("[BrowserVM] WebView or CoreWebView2 not initialized for navigation.");
                        StatusChanged?.Invoke(this, "Browser component not ready for navigation.");
                    }
                }
                catch (ArgumentException ex) // Catch specific exceptions like invalid URI format
                {
                     Debug.WriteLine($"[BrowserVM] Invalid URL format: {urlString} - {ex.Message}");
                     StatusChanged?.Invoke(this, $"Invalid URL: {urlString}");
                }
                catch (Exception ex) // Catch other potential navigation errors
                {
                    Debug.WriteLine($"[BrowserVM] Navigation error: {ex.Message}");
                    StatusChanged?.Invoke(this, $"Error navigating to URL: {ex.Message}");
                }
            }
        }

        // NEW Cleanup method
        public void Cleanup()
        {
            _navigationService.StatusChanged -= NavigationService_StatusChanged;
            _navigationService.NavigationStateChanged -= NavigationService_NavigationStateChanged;
            _navigationService.ModUrlValidityChanged -= NavigationService_ModUrlValidityChanged;
            _navigationService.NavigationSucceededAndUrlValid -= NavigationService_NavigationSucceededAndUrlValid;

            if (_extractorService != null)
            {
                _extractorService.IsModInfoAvailableChanged -= ExtractorService_IsModInfoAvailableChanged;
                // Assuming IModExtractorService doesn't need explicit disposal
            }
            // Clear WebView reference if needed, though parent usually owns it
            // _webView = null;
             Debug.WriteLine("BrowserViewModel cleaned up.");
        }
    }
}