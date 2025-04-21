#nullable enable
using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using RimSharp.Core.Commands.Base; // Added for DelegateCommand
using RimSharp.Features.WorkshopDownloader.ViewModels; // For DownloaderViewModel
using RimSharp.AppDir.AppFiles; // For ViewModelBase
using RimSharp.Shared.Services.Contracts; // For services if needed (like IDialogService - though not used directly here)
using System.Diagnostics;
using RimSharp.Features.WorkshopDownloader.Services; // For IWebNavigationService, IModExtractorService
using RimSharp.Features.WorkshopDownloader.Models; // For ModInfoDto
using System.Threading.Tasks;
using System.Threading;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    /// <summary>
    /// ViewModel for the browser component, handling navigation, URL display, and mod info extraction triggering.
    /// </summary>
    public class BrowserViewModel : ViewModelBase, IDisposable
    {
        private readonly IWebNavigationService _navigationService;
        private readonly DownloaderViewModel _parentViewModel;
        private WebView2? _webView; // Keep a reference for potential direct manipulation if needed (e.g., fallback navigation)
        private IModExtractorService? _extractorService; // Instance specific to this ViewModel

        // --- Properties ---

        private bool _canGoBack;
        /// <summary>
        /// Gets a value indicating whether the browser can navigate back.
        /// </summary>
        public bool CanGoBack { get => _canGoBack; private set => SetProperty(ref _canGoBack, value); }

        private bool _canGoForward;
        /// <summary>
        /// Gets a value indicating whether the browser can navigate forward.
        /// </summary>
        public bool CanGoForward { get => _canGoForward; private set => SetProperty(ref _canGoForward, value); }

        private bool _isValidModUrl;
        /// <summary>
        /// Gets a value indicating whether the current URL is recognized as a valid Steam Workshop mod page URL.
        /// </summary>
        public bool IsValidModUrl { get => _isValidModUrl; private set => SetProperty(ref _isValidModUrl, value); }

        private string _actualCurrentUrl = string.Empty;
        /// <summary>
        /// Gets the actual current URL reported by the navigation service.
        /// </summary>
        public string ActualCurrentUrl { get => _actualCurrentUrl; private set => SetProperty(ref _actualCurrentUrl, value); }

        private string _addressBarUrl = string.Empty;
        /// <summary>
        /// Gets or sets the text displayed in the address bar. This is bound to the TextBox.
        /// </summary>
        public string AddressBarUrl { get => _addressBarUrl; set => SetProperty(ref _addressBarUrl, value); }

        private bool _isModInfoAvailable;
        /// <summary>
        /// Gets a value indicating whether mod information (like title, ID) could be extracted from the current page.
        /// </summary>
        public bool IsModInfoAvailable { get => _isModInfoAvailable; private set => SetProperty(ref _isModInfoAvailable, value); }

        private bool _isOperationInProgress;
        /// <summary>
        /// Gets a value indicating whether a long-running operation (like downloading) is in progress in the parent context.
        /// Affects command CanExecute states.
        /// </summary>
        public bool IsOperationInProgress { get => _isOperationInProgress; private set => SetProperty(ref _isOperationInProgress, value); }

        // --- Commands ---
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand NavigateToUrlCommand { get; }

        // --- Events ---
        /// <summary>
        /// Raised when the status message changes (e.g., loading, loaded, error).
        /// </summary>
        public event EventHandler<string>? StatusChanged;
        /// <summary>
        /// Raised when the availability of extractable mod info on the current page changes.
        /// </summary>
        public event EventHandler? ModInfoAvailabilityChanged;

        // --- Named Event Handlers for Unsubscribing ---
        private EventHandler<string>? _navStatusHandler;
        private EventHandler? _navStateChangedHandler;
        private EventHandler<bool>? _navModUrlValidityHandler;
        private EventHandler<string>? _navSucceededAndValidHandler;
        private EventHandler<string>? _navSourceUrlChangedHandler; // Handler for SourceUrlChanged event
        private EventHandler? _extractorModInfoAvailableHandler;
        private PropertyChangedEventHandler? _parentPropertyChangedHandler;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrowserViewModel"/> class.
        /// </summary>
        /// <param name="navigationService">The web navigation service.</param>
        /// <param name="parentViewModel">The parent DownloaderViewModel.</param>
        public BrowserViewModel(IWebNavigationService navigationService, DownloaderViewModel parentViewModel) : base() // Call base constructor
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _parentViewModel = parentViewModel ?? throw new ArgumentNullException(nameof(parentViewModel));

            // Initialize properties from service/parent state
            _canGoBack = _navigationService.CanGoBack;
            _canGoForward = _navigationService.CanGoForward;
            _isValidModUrl = _navigationService.IsValidModUrl;
            _actualCurrentUrl = _navigationService.CurrentUrl ?? string.Empty;
            _addressBarUrl = _actualCurrentUrl; // Initialize address bar with current URL
            _isOperationInProgress = _parentViewModel.IsOperationInProgress;

            // Subscribe to navigation service events
            _navStatusHandler = NavigationService_StatusChanged;
            _navigationService.StatusChanged += _navStatusHandler;

            _navStateChangedHandler = NavigationService_NavigationStateChanged; // Handles CanGoBack/Forward
            _navigationService.NavigationStateChanged += _navStateChangedHandler;

            _navModUrlValidityHandler = NavigationService_ModUrlValidityChanged;
            _navigationService.ModUrlValidityChanged += _navModUrlValidityHandler;

            _navSucceededAndValidHandler = NavigationService_NavigationSucceededAndUrlValid; // Triggers info extraction
            _navigationService.NavigationSucceededAndUrlValid += _navSucceededAndValidHandler;

            _navSourceUrlChangedHandler = NavigationService_SourceUrlChanged; // Updates AddressBarUrl
            _navigationService.SourceUrlChanged += _navSourceUrlChangedHandler;

            // Subscribe to parent VM's IsOperationInProgress changes
            _parentPropertyChangedHandler = ParentViewModel_PropertyChanged;
            _parentViewModel.PropertyChanged += _parentPropertyChangedHandler;

            // Initialize commands using ViewModelBase helpers
            GoBackCommand = CreateCommand(
                execute: () => _navigationService.GoBack(),
                canExecute: () => !IsOperationInProgress && CanGoBack,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(CanGoBack) }
            );

            GoForwardCommand = CreateCommand(
                execute: () => _navigationService.GoForward(),
                canExecute: () => !IsOperationInProgress && CanGoForward,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(CanGoForward) }
            );

            GoHomeCommand = CreateCommand(
                execute: () => _navigationService.GoHome(),
                canExecute: () => !IsOperationInProgress,
                observedProperties: new[] { nameof(IsOperationInProgress) }
            );

            NavigateToUrlCommand = CreateCommand<string>(
                execute: ExecuteNavigateToUrlCommand,
                canExecute: url => !IsOperationInProgress && !string.IsNullOrWhiteSpace(url),
                observedProperties: new[] { nameof(IsOperationInProgress) } // Only needs to observe busy state
            );

            Debug.WriteLine("[BrowserVM] Initialized.");
        }

        /// <summary>
        /// Sets the WebView2 control instance for this ViewModel to interact with.
        /// </summary>
        /// <param name="webView">The WebView2 control.</param>
        public void SetWebView(WebView2 webView)
        {
            if (_disposed) // Use base class _disposed
            {
                Debug.WriteLine("[BrowserVM] SetWebView called but already disposed.");
                return;
            }
             if (webView == null)
             {
                  Debug.WriteLine("[BrowserVM] SetWebView called with null WebView.");
                  // Potentially throw or handle this error state
                  return;
             }

            _webView = webView; // Store reference

            // --- Setup ModExtractorService ---
            // Dispose the old extractor if replacing the WebView
            if (_extractorService != null)
            {
                 if (_extractorModInfoAvailableHandler != null)
                     _extractorService.IsModInfoAvailableChanged -= _extractorModInfoAvailableHandler;
                (_extractorService as IDisposable)?.Dispose();
                 _extractorService = null;
                 Debug.WriteLine("[BrowserVM] Disposed previous ModExtractorService.");
            }
            // Create and setup the new extractor
            _extractorService = new ModExtractorService(webView);
            _extractorModInfoAvailableHandler = ExtractorService_IsModInfoAvailableChanged;
            _extractorService.IsModInfoAvailableChanged += _extractorModInfoAvailableHandler;
            Debug.WriteLine("[BrowserVM] Created and subscribed to new ModExtractorService.");
            // --- End ModExtractorService Setup ---


            // --- Pass WebView to Navigation Service ---
            // This is crucial: it hooks up the core WebView2 events within the service
            _navigationService.SetWebView(webView);
            // The service will now raise SourceUrlChanged, NavigationStateChanged etc. based on the webView events.
            Debug.WriteLine("[BrowserVM] Passed WebView to NavigationService.");


            // --- Update internal state ---
            RunOnUIThread(() =>
            {
                // Update extractor state based on the *new* instance
                IsModInfoAvailable = _extractorService?.IsModInfoAvailable ?? false;
                ModInfoAvailabilityChanged?.Invoke(this, EventArgs.Empty);

                // AddressBarUrl will be updated by the NavigationService_SourceUrlChanged handler
                // when the service raises the event after SetWebView connects.
                // Update CanGoBack/Forward based on initial state from service after SetWebView
                CanGoBack = _navigationService.CanGoBack;
                CanGoForward = _navigationService.CanGoForward;
            });
        }

        /// <summary>
        /// Attempts to extract mod information from the current page asynchronously.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation, containing the extracted ModInfoDto or null.</returns>
        public Task<ModInfoDto?> GetCurrentModInfoAsync(CancellationToken token = default)
        {
            if (_disposed)
            {
                 Debug.WriteLine("[BrowserVM] GetCurrentModInfoAsync called but disposed.");
                 return Task.FromResult<ModInfoDto?>(null);
            }
            if (_extractorService == null)
            {
                Debug.WriteLine("[BrowserVM] GetCurrentModInfoAsync: Mod extractor not initialized.");
                StatusChanged?.Invoke(this, "Mod extractor not initialized.");
                return Task.FromResult<ModInfoDto?>(null);
            }

            try
            {
                Debug.WriteLine("[BrowserVM] Calling _extractorService.ExtractFullModInfo().");
                // Use the ModExtractorService instance owned by this ViewModel
                return _extractorService.ExtractFullModInfo(); // Note: This method itself might need CancellationToken support
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Error calling ExtractFullModInfo: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error extracting mod info: {ex.Message}");
                return Task.FromResult<ModInfoDto?>(null);
            }
        }

        // --- Event Handlers ---

        private void ParentViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed) return;
            if (e.PropertyName == nameof(DownloaderViewModel.IsOperationInProgress))
            {
                // Update local property and notify commands
                RunOnUIThread(() =>
                {
                    IsOperationInProgress = _parentViewModel.IsOperationInProgress;
                    Debug.WriteLine($"[BrowserVM] Parent IsOperationInProgress changed: {IsOperationInProgress}");
                    // Command CanExecute changes are handled by the base class observation mechanism
                });
            }
        }

        private void NavigationService_StatusChanged(object? sender, string message)
        {
            if (_disposed) return;
            StatusChanged?.Invoke(this, message); // Forward status messages
        }

        // Handles the event signaling the WebView's Source URL has changed
        private void NavigationService_SourceUrlChanged(object? sender, string newUrl)
        {
            if (_disposed) return;
            Debug.WriteLine($"[BrowserVM] Handling SourceUrlChanged: {newUrl}");
            RunOnUIThread(() =>
            {
                ActualCurrentUrl = newUrl; // Update internal tracker
                AddressBarUrl = newUrl;    // Directly update the address bar text property
                Debug.WriteLine($"[BrowserVM] Updated AddressBarUrl to: {AddressBarUrl}");
            });
        }

        // Handles the event signaling changes in CanGoBack/CanGoForward state
        private void NavigationService_NavigationStateChanged(object? sender, EventArgs e)
        {
            if (_disposed) return;
            Debug.WriteLine("[BrowserVM] Handling NavigationStateChanged (for CanGoBack/Forward)");
            RunOnUIThread(() =>
            {
                bool changed = false;
                if (CanGoBack != _navigationService.CanGoBack)
                {
                    CanGoBack = _navigationService.CanGoBack;
                    changed = true;
                }
                if (CanGoForward != _navigationService.CanGoForward)
                {
                     CanGoForward = _navigationService.CanGoForward;
                     changed = true;
                }

                if(changed)
                    Debug.WriteLine($"[BrowserVM] Updated CanGoBack: {CanGoBack}, CanGoForward: {CanGoForward}");

                // Command CanExecute updates handled by base class observation
            });
        }

        // Handles the event signaling changes in whether the current URL is a valid mod URL
        private void NavigationService_ModUrlValidityChanged(object? sender, bool isValid)
        {
            if (_disposed) return;
            Debug.WriteLine($"[BrowserVM] Handling ModUrlValidityChanged: {isValid}");
            RunOnUIThread(() => IsValidModUrl = isValid);
        }

        // Handles the event signaling navigation succeeded AND the URL is a valid mod URL
        // Primary purpose here is to trigger automatic mod info extraction.
        private async void NavigationService_NavigationSucceededAndUrlValid(object? sender, string url)
        {
            if (_disposed) return;
            if (_extractorService == null)
            {
                 Debug.WriteLine("[BrowserVM] NavigationSucceededAndUrlValid: Extractor service is null, skipping extraction.");
                 return;
            }
            // AddressBarUrl should already be up-to-date via SourceUrlChanged handler.

            Debug.WriteLine($"[BrowserVM] Handling NavigationSucceededAndUrlValid for URL ({url}). Triggering info extraction.");
            StatusChanged?.Invoke(this, "Extracting mod details...");
            try
            {
                // No need to pass token here unless GetCurrentModInfoAsync accepts one and cancellation is desired
                await GetCurrentModInfoAsync();
                // Status update might happen inside GetCurrentModInfoAsync or based on its result if needed
                 StatusChanged?.Invoke(this, "Mod details extracted (if available).");
            }
            catch (Exception ex) // Catch potential exceptions from GetCurrentModInfoAsync or the await
            {
                Debug.WriteLine($"[BrowserVM] Extraction failed after NavSucceededAndValid: {ex.Message}");
                RunOnUIThread(() => StatusChanged?.Invoke(this, $"Error during automatic info extraction: {ex.Message}"));
            }
        }

        // Handles changes in mod info availability from the extractor service
        private void ExtractorService_IsModInfoAvailableChanged(object? sender, EventArgs e)
        {
            if (_disposed || _extractorService == null) return;
            RunOnUIThread(() =>
            {
                bool newValue = _extractorService.IsModInfoAvailable;
                if (IsModInfoAvailable != newValue)
                {
                    IsModInfoAvailable = newValue;
                    Debug.WriteLine($"[BrowserVM] Handling ExtractorService_IsModInfoAvailableChanged. IsModInfoAvailable: {IsModInfoAvailable}");
                    ModInfoAvailabilityChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        // --- Command Logic ---

        // Executes navigation based on the URL provided (typically from Address Bar Enter key)
        private void ExecuteNavigateToUrlCommand(string? urlString)
        {
            if (_disposed) return;

            if (string.IsNullOrWhiteSpace(urlString))
            {
                Debug.WriteLine("[BrowserVM] ExecuteNavigateToUrlCommand: URL is null or empty.");
                StatusChanged?.Invoke(this, "Please enter a valid URL.");
                return;
            }

            string processedUrl = urlString.Trim(); // Trim whitespace

            // Basic check: Add scheme if missing (default to https)
            if (!processedUrl.Contains("://") && !processedUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                processedUrl = "https://" + processedUrl;
                Debug.WriteLine($"[BrowserVM] Prepended https:// to URL: {processedUrl}");
            }

            Debug.WriteLine($"[BrowserVM] Attempting to navigate to: {processedUrl}");
            StatusChanged?.Invoke(this, $"Navigating to {processedUrl}...");

            try
            {
                // Prefer using CoreWebView2.Navigate for robustness
                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Navigate(processedUrl);
                    // Navigation state/URL updates will be handled by the service events
                }
                // Fallback: Use Source property if CoreWebView2 isn't ready (less ideal)
                else if (_webView != null && Uri.TryCreate(processedUrl, UriKind.Absolute, out var uri))
                {
                    _webView.Source = uri; // Setting Source triggers SourceChanged event
                    Debug.WriteLine("[BrowserVM] Warning: Navigating via Source property (CoreWebView2 might not be ready).");
                }
                else
                {
                    Debug.WriteLine("[BrowserVM] WebView or CoreWebView2 not available/initialized, cannot navigate.");
                    StatusChanged?.Invoke(this, "Browser component not ready for navigation.");
                }
            }
            catch (ArgumentException ex) // Catch invalid URI formats specifically
            {
                Debug.WriteLine($"[BrowserVM] Invalid URL format during navigation: {processedUrl} - {ex.Message}");
                StatusChanged?.Invoke(this, $"Invalid URL format: {urlString}"); // Show original user input in error
            }
            catch (Exception ex) // Catch other general navigation errors
            {
                Debug.WriteLine($"[BrowserVM] Navigation error for URL {processedUrl}: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error navigating: {ex.Message}");
            }
        }


        // --- Dispose Implementation ---

        /// <summary>
        /// Releases resources used by the ViewModel.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from the finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            // Use the inherited _disposed field check FIRST
            if (_disposed)
            {
                 return;
            }

            if (disposing) // Only dispose managed resources if called from Dispose()
            {
                // --- Derived Class Specific Managed Resource Cleanup ---
                Debug.WriteLine("[BrowserVM] Disposing derived managed resources...");

                // Unsubscribe from Navigation Service events
                if (_navigationService != null)
                {
                    if (_navStatusHandler != null) _navigationService.StatusChanged -= _navStatusHandler;
                    if (_navStateChangedHandler != null) _navigationService.NavigationStateChanged -= _navStateChangedHandler;
                    if (_navModUrlValidityHandler != null) _navigationService.ModUrlValidityChanged -= _navModUrlValidityHandler;
                    if (_navSucceededAndValidHandler != null) _navigationService.NavigationSucceededAndUrlValid -= _navSucceededAndValidHandler;
                    if (_navSourceUrlChangedHandler != null) _navigationService.SourceUrlChanged -= _navSourceUrlChangedHandler; // Unsubscribe new handler
                    Debug.WriteLine("[BrowserVM] Unsubscribed from NavigationService events.");
                }

                // Unsubscribe from and dispose the owned ModExtractorService
                if (_extractorService != null)
                {
                    if (_extractorModInfoAvailableHandler != null)
                        _extractorService.IsModInfoAvailableChanged -= _extractorModInfoAvailableHandler;
                    (_extractorService as IDisposable)?.Dispose(); // Dispose if it implements IDisposable
                    _extractorService = null; // Clear reference
                    Debug.WriteLine("[BrowserVM] Unsubscribed from and disposed ModExtractorService.");
                }

                // Unsubscribe from Parent ViewModel events
                if (_parentViewModel != null && _parentPropertyChangedHandler != null)
                {
                    _parentViewModel.PropertyChanged -= _parentPropertyChangedHandler;
                    Debug.WriteLine("[BrowserVM] Unsubscribed from ParentViewModel PropertyChanged.");
                }

                // Clear handler references to help garbage collection
                _navStatusHandler = null;
                _navStateChangedHandler = null;
                _navModUrlValidityHandler = null;
                _navSucceededAndValidHandler = null;
                _navSourceUrlChangedHandler = null;
                _extractorModInfoAvailableHandler = null;
                _parentPropertyChangedHandler = null;

                _webView = null; // Clear reference to the UI control

                Debug.WriteLine("[BrowserVM] Dispose complete (derived managed resources).");
                // --- End Derived Class Specific Managed Resource Cleanup ---
            }

            // Dispose unmanaged resources here (if BrowserViewModel directly owned any - unlikely)

            // IMPORTANT: Call the base class implementation LAST
            // This will set the _disposed flag and dispose commands created by CreateCommand helpers.
            Debug.WriteLine($"[BrowserVM] Calling base.Dispose({disposing})...");
            base.Dispose(disposing);
            Debug.WriteLine($"[BrowserVM] Finished Dispose({disposing}). Base class disposed state: {_disposed}");
        }

         // Finalizer (optional, only needed if BrowserViewModel DIRECTLY owns unmanaged resources)
         ~BrowserViewModel()
         {
             Debug.WriteLine("[BrowserVM] Finalizer called. Disposing unmanaged resources.");
             Dispose(false); // Dispose only unmanaged resources from finalizer
         }
    }
}