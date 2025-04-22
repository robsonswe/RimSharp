#nullable enable
using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Threading.Tasks;
using System.Threading;
using System.Web; // For HttpUtility.UrlEncode
using System.Net;
using System.Collections.Generic; // For WebUtility.UrlEncode (alternative)

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class BrowserViewModel : ViewModelBase, IDisposable
    {
        private readonly IWebNavigationService _navigationService;
        private readonly DownloaderViewModel _parentViewModel;
        private WebView2? _webView;
        private IModExtractorService? _extractorService;

        // --- Properties ---
        private bool _canGoBack;
        public bool CanGoBack { get => _canGoBack; private set => SetProperty(ref _canGoBack, value); }

        private bool _canGoForward;
        public bool CanGoForward { get => _canGoForward; private set => SetProperty(ref _canGoForward, value); }

        private bool _isValidModUrl;
        // Make setter public or internal if DownloadQueueViewModel needs to set it,
        // but preferably keep private and let events handle updates.
        // Raising PropertyChanged is essential for DownloadQueueViewModel to react.
        public bool IsValidModUrl
        {
            get => _isValidModUrl;
            private set => SetProperty(ref _isValidModUrl, value);
        }

        private bool _isCollectionUrl;
        // Raising PropertyChanged is essential for DownloadQueueViewModel to react.
        public bool IsCollectionUrl
        {
            get => _isCollectionUrl;
            private set => SetProperty(ref _isCollectionUrl, value);
        }


        private string _actualCurrentUrl = string.Empty; // Last *confirmed* URL
        public string ActualCurrentUrl { get => _actualCurrentUrl; private set => SetProperty(ref _actualCurrentUrl, value); }

        private string _addressBarUrl = string.Empty; // URL shown in the address bar (updated early)
        public string AddressBarUrl { get => _addressBarUrl; set => SetProperty(ref _addressBarUrl, value); }

        // --- New Properties ---
        private bool _isLoading;
        /// <summary>
        /// Gets a value indicating whether the browser is currently loading a page.
        /// </summary>
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private bool _isSecure; // Basic HTTPS indicator
        /// <summary>
        /// Gets a value indicating whether the current URL is HTTPS.
        /// </summary>
        public bool IsSecure { get => _isSecure; private set => SetProperty(ref _isSecure, value); }

        private string _searchText = string.Empty;
        /// <summary>
        /// Gets or sets the text entered in the search box.
        /// </summary>
        public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
        // --- End New Properties ---

        private bool _isModInfoAvailable;
        // Raising PropertyChanged is essential for DownloadQueueViewModel to react.
        public bool IsModInfoAvailable
        {
            get => _isModInfoAvailable;
            private set => SetProperty(ref _isModInfoAvailable, value);
        }


        private bool _isOperationInProgress; // From parent
        // This property might still be useful for disabling browser navigation buttons
        public bool IsOperationInProgress { get => _isOperationInProgress; private set => SetProperty(ref _isOperationInProgress, value); }

        // ---- CanAddMod Property REMOVED ----
        // private bool _canAddMod;
        // public bool CanAddMod { get => _canAddMod; private set => SetProperty(ref _canAddMod, value); }

        // --- Commands ---
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand RefreshCommand { get; } // Added Refresh command
        public ICommand NavigateToUrlCommand { get; }
        public ICommand SearchCommand { get; } // Added Search command

        // --- Events ---
        public event EventHandler<string>? StatusChanged;
        // ---- ModInfoAvailabilityChanged event REMOVED (or repurposed if needed elsewhere) ----
        // PropertyChanged on IsModInfoAvailable is now the primary notification mechanism
        // public event EventHandler? ModInfoAvailabilityChanged;

        // --- Named Event Handlers ---
        private EventHandler<string>? _navStatusHandler;
        private EventHandler? _navStateChangedHandler;
        private EventHandler<bool>? _navModUrlValidityHandler;
        private EventHandler<bool>? _navCollectionUrlValidityHandler;
        private EventHandler<string>? _navSucceededAndValidHandler;
        private EventHandler<string>? _navSourceUrlChangedHandler;
        private EventHandler? _extractorModInfoAvailableHandler; // Still needed to update IsModInfoAvailable property
        private PropertyChangedEventHandler? _parentPropertyChangedHandler;
        // New event handlers
        private EventHandler<string>? _navStartedHandler;
        private EventHandler? _navEndedHandler;


        public BrowserViewModel(IWebNavigationService navigationService, DownloaderViewModel parentViewModel) : base()
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _parentViewModel = parentViewModel ?? throw new ArgumentNullException(nameof(parentViewModel));

            // Initialize properties from service/parent state
            _canGoBack = _navigationService.CanGoBack;
            _canGoForward = _navigationService.CanGoForward;
            _isValidModUrl = _navigationService.IsValidModUrl;
            _isCollectionUrl = _navigationService.IsCollectionUrl;
            _actualCurrentUrl = _navigationService.CurrentUrl ?? string.Empty;
            _addressBarUrl = _actualCurrentUrl;
            IsSecure = _actualCurrentUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            _isOperationInProgress = _parentViewModel.IsOperationInProgress;
            // _isModInfoAvailable is initialized when extractor is set/changes

            // Subscribe to navigation service events
            _navStatusHandler = NavigationService_StatusChanged;
            _navigationService.StatusChanged += _navStatusHandler;

            _navStateChangedHandler = NavigationService_NavigationStateChanged;
            _navigationService.NavigationStateChanged += _navStateChangedHandler;

            _navModUrlValidityHandler = NavigationService_ModUrlValidityChanged;
            _navigationService.ModUrlValidityChanged += _navModUrlValidityHandler;

            _navCollectionUrlValidityHandler = NavigationService_CollectionUrlValidityChanged;
            _navigationService.CollectionUrlValidityChanged += _navCollectionUrlValidityHandler; // Ensure this is subscribed

            _navSucceededAndValidHandler = NavigationService_NavigationSucceededAndUrlValid;
            _navigationService.NavigationSucceededAndUrlValid += _navSucceededAndValidHandler;

            _navSourceUrlChangedHandler = NavigationService_SourceUrlChanged; // Handles *confirmed* URL changes
            _navigationService.SourceUrlChanged += _navSourceUrlChangedHandler;

            // --- Subscribe to new events ---
            _navStartedHandler = NavigationService_NavigationStarted;
            _navigationService.NavigationStarted += _navStartedHandler;

            _navEndedHandler = NavigationService_NavigationEnded;
            _navigationService.NavigationEnded += _navEndedHandler;
            // --- End Subscribe ---


            _parentPropertyChangedHandler = ParentViewModel_PropertyChanged;
            _parentViewModel.PropertyChanged += _parentPropertyChangedHandler;

            // --- Initialize Commands (Observe IsOperationInProgress/IsLoading for disabling UI) ---
            GoBackCommand = CreateCommand(
                execute: () => _navigationService.GoBack(),
                canExecute: () => !IsLoading && !IsOperationInProgress && CanGoBack,
                observedProperties: new[] { nameof(IsLoading), nameof(IsOperationInProgress), nameof(CanGoBack) }
            );

            GoForwardCommand = CreateCommand(
                execute: () => _navigationService.GoForward(),
                canExecute: () => !IsLoading && !IsOperationInProgress && CanGoForward,
                observedProperties: new[] { nameof(IsLoading), nameof(IsOperationInProgress), nameof(CanGoForward) }
            );

            GoHomeCommand = CreateCommand(
                execute: () => _navigationService.GoHome(),
                canExecute: () => !IsLoading && !IsOperationInProgress,
                observedProperties: new[] { nameof(IsLoading), nameof(IsOperationInProgress) }
            );

            RefreshCommand = CreateCommand( // Define Refresh Command
                execute: () => _webView?.CoreWebView2?.Reload(),
                canExecute: () => !IsLoading && !IsOperationInProgress && _webView?.CoreWebView2 != null,
                observedProperties: new[] { nameof(IsLoading), nameof(IsOperationInProgress) } // Add dependency if needed
            );

            NavigateToUrlCommand = CreateCommand<string>(
                execute: ExecuteNavigateToUrlCommand, // Logic now handles search-or-go
                canExecute: url => !IsLoading && !IsOperationInProgress && !string.IsNullOrWhiteSpace(url),
                observedProperties: new[] { nameof(IsLoading), nameof(IsOperationInProgress) }
            );

            SearchCommand = CreateCommand( // Define Search Command
                execute: ExecuteSearchCommand,
                canExecute: () => !IsLoading && !IsOperationInProgress && !string.IsNullOrWhiteSpace(SearchText),
                observedProperties: new[] { nameof(IsLoading), nameof(IsOperationInProgress), nameof(SearchText) }
            );
            // --- End Initialize Commands ---
            // ---- CalculateCanAddMod() call REMOVED ----

            Debug.WriteLine("[BrowserVM] Initialized.");
        }

        public void SetWebView(WebView2 webView)
        {
            if (_disposed || webView == null) return;
            _webView = webView;

            // Dispose old extractor if exists
            if (_extractorService != null)
            {
                if (_extractorModInfoAvailableHandler != null)
                    _extractorService.IsModInfoAvailableChanged -= _extractorModInfoAvailableHandler;
                (_extractorService as IDisposable)?.Dispose();
                _extractorService = null;
                Debug.WriteLine("[BrowserVM] Disposed previous ModExtractorService.");
            }

            // Create and setup the new extractor
            try
            {
                _extractorService = new ModExtractorService(webView); // Ensure this constructor is robust
                _extractorModInfoAvailableHandler = ExtractorService_IsModInfoAvailableChanged;
                _extractorService.IsModInfoAvailableChanged += _extractorModInfoAvailableHandler;
                Debug.WriteLine("[BrowserVM] Created and subscribed to new ModExtractorService.");

                // Set initial state after creating extractor
                IsModInfoAvailable = _extractorService?.IsModInfoAvailable ?? false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Error creating ModExtractorService: {ex.Message}");
                IsModInfoAvailable = false; // Ensure state is false on error
            }


            _navigationService.SetWebView(webView); // Hook up service
            Debug.WriteLine("[BrowserVM] Passed WebView to NavigationService.");

            RunOnUIThread(() =>
            {
                // Update nav state from service after setting WebView
                CanGoBack = _navigationService.CanGoBack;
                CanGoForward = _navigationService.CanGoForward;
                // Refresh command CanExecute state if WebView reference changes
                (RefreshCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            });
        }

        public Task<List<CollectionItemInfo>> ExtractCollectionItemsAsync(CancellationToken token = default)
        {
            if (_disposed || _extractorService == null)
            {
                Debug.WriteLine($"[BrowserVM] ExtractCollectionItemsAsync called but disposed({_disposed}) or extractor is null.");
                return Task.FromResult(new List<CollectionItemInfo>());
            }
            try
            {
                Debug.WriteLine("[BrowserVM] Calling _extractorService.ExtractCollectionItemsAsync().");
                return _extractorService.ExtractCollectionItemsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Error calling ExtractCollectionItemsAsync: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error extracting collection items: {ex.Message}");
                return Task.FromResult(new List<CollectionItemInfo>());
            }
        }

        public Task<ModInfoDto?> GetCurrentModInfoAsync(CancellationToken token = default)
        {
            if (_disposed || _extractorService == null)
            {
                Debug.WriteLine($"[BrowserVM] GetCurrentModInfoAsync called but disposed({_disposed}) or extractor is null.");
                return Task.FromResult<ModInfoDto?>(null);
            }

            try
            {
                Debug.WriteLine("[BrowserVM] Calling _extractorService.ExtractFullModInfo().");
                return _extractorService.ExtractFullModInfo();
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

            // Still need to react to parent's IsOperationInProgress for browser UI (nav buttons)
            if (e.PropertyName == nameof(DownloaderViewModel.IsOperationInProgress))
            {
                RunOnUIThread(() =>
                {
                    IsOperationInProgress = _parentViewModel.IsOperationInProgress;
                    Debug.WriteLine($"[BrowserVM] Parent IsOperationInProgress changed: {IsOperationInProgress}");
                    // Note: Commands observing IsOperationInProgress will update automatically
                });
            }
            // ---- Handling for Parent IsSteamCmdReady REMOVED (not needed for CanAddMod here) ----
            // else if (e.PropertyName == nameof(DownloaderViewModel.IsSteamCmdReady))
            // {
            //      Debug.WriteLine($"[BrowserVM] Parent IsSteamCmdReady changed: {_parentViewModel.IsSteamCmdReady}");
            //      // No longer recalculating CanAddMod here
            // }
        }


        private void NavigationService_StatusChanged(object? sender, string message)
        {
            if (_disposed) return;
            RunOnUIThread(() => StatusChanged?.Invoke(this, message));
        }

        // Handles the NavigationStarted event from the service
        private void NavigationService_NavigationStarted(object? sender, string intendedUrl)
        {
            if (_disposed) return;
            Debug.WriteLine($"[BrowserVM] Handling NavigationStarted: {intendedUrl}");
            RunOnUIThread(() =>
            {
                IsLoading = true;
                AddressBarUrl = intendedUrl; // <<<< UPDATE ADDRESS BAR EARLY
                IsSecure = intendedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                // Reset validity flags during navigation start? Optional.
                // IsValidModUrl = false;
                // IsCollectionUrl = false;
                // IsModInfoAvailable = false; // Might be too early, wait for extractor
                Debug.WriteLine($"[BrowserVM] Updated AddressBarUrl to: {AddressBarUrl}, IsLoading: {IsLoading}, IsSecure: {IsSecure}");
            });
        }

        // Handles the NavigationEnded event from the service
        private void NavigationService_NavigationEnded(object? sender, EventArgs e)
        {
            if (_disposed) return;
            Debug.WriteLine("[BrowserVM] Handling NavigationEnded.");
            RunOnUIThread(() =>
            {
                IsLoading = false;
                Debug.WriteLine($"[BrowserVM] IsLoading set to: {IsLoading}");
            });
        }


        // Handles *confirmed* source URL changes (after navigation completes or history changes)
        private void NavigationService_SourceUrlChanged(object? sender, string newUrl)
        {
            if (_disposed) return;
            Debug.WriteLine($"[BrowserVM] Handling *Confirmed* SourceUrlChanged: {newUrl}");
            RunOnUIThread(() =>
            {
                ActualCurrentUrl = newUrl;
                if (AddressBarUrl != newUrl && !IsLoading)
                {
                    AddressBarUrl = newUrl;
                    Debug.WriteLine($"[BrowserVM] Corrected AddressBarUrl based on confirmed source: {AddressBarUrl}");
                }
                IsSecure = newUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            });
        }

        private void NavigationService_NavigationStateChanged(object? sender, EventArgs e)
        {
            if (_disposed) return;
            RunOnUIThread(() =>
            {
                CanGoBack = _navigationService.CanGoBack;
                CanGoForward = _navigationService.CanGoForward;
            });
        }

        // Just updates the property. DownloadQueueViewModel listens for PropertyChanged.
        private void NavigationService_ModUrlValidityChanged(object? sender, bool isValid)
        {
            if (_disposed) return;
            Debug.WriteLine($"[BrowserVM] Handling ModUrlValidityChanged: {isValid}");
            RunOnUIThread(() => {
                IsValidModUrl = isValid;
                // ---- CalculateCanAddMod() call REMOVED ----
            });
        }

        // Just updates the property. DownloadQueueViewModel listens for PropertyChanged.
        private void NavigationService_CollectionUrlValidityChanged(object? sender, bool isValid)
        {
            if (_disposed) return;
            Debug.WriteLine($"[BrowserVM] Handling CollectionUrlValidityChanged: {isValid}");
            RunOnUIThread(() => {
                IsCollectionUrl = isValid;
                // ---- CalculateCanAddMod() call REMOVED ----
            });
        }


        // Still useful to trigger extraction on successful navigation
        private async void NavigationService_NavigationSucceededAndUrlValid(object? sender, string url)
        {
            if (_disposed || _extractorService == null) return;
            Debug.WriteLine($"[BrowserVM] Handling NavigationSucceededAndUrlValid for URL ({url}). Triggering info extraction.");
            RunOnUIThread(() => StatusChanged?.Invoke(this, "Extracting mod details..."));
            try
            {
                // Get info, which will trigger ExtractorService_IsModInfoAvailableChanged if needed
                await GetCurrentModInfoAsync();
                RunOnUIThread(() => StatusChanged?.Invoke(this, "Mod details extracted (if available)."));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Extraction failed after NavSucceededAndValid: {ex.Message}");
                RunOnUIThread(() => StatusChanged?.Invoke(this, $"Error during automatic info extraction: {ex.Message}"));
            }
        }

        // Updates the IsModInfoAvailable property. DownloadQueueViewModel listens for PropertyChanged.
        private void ExtractorService_IsModInfoAvailableChanged(object? sender, EventArgs e)
        {
            if (_disposed || _extractorService == null) return;
            RunOnUIThread(() =>
            {
                bool newValue = _extractorService.IsModInfoAvailable;
                // Directly set property - SetProperty handles change check and notification
                IsModInfoAvailable = newValue;
                Debug.WriteLine($"[BrowserVM] Handling ExtractorService_IsModInfoAvailableChanged. IsModInfoAvailable: {IsModInfoAvailable}");
            });
        }


        // --- Command Logic (Unchanged) ---

        private void ExecuteNavigateToUrlCommand(string? urlString)
        {
            if (_disposed || string.IsNullOrWhiteSpace(urlString)) return;

            string processedInput = urlString.Trim();
            bool isUrl = Uri.TryCreate(processedInput, UriKind.Absolute, out var uriResult)
                         && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps || uriResult.Scheme == Uri.UriSchemeFtp);

            if (!isUrl && processedInput.Contains('.') && !processedInput.Contains(' ') && !processedInput.StartsWith("about:"))
            {
                string potentialUrl = "https://" + processedInput;
                if (Uri.TryCreate(potentialUrl, UriKind.Absolute, out uriResult)
                    && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    processedInput = potentialUrl;
                    isUrl = true;
                    Debug.WriteLine($"[BrowserVM] Prepended https:// to likely domain: {processedInput}");
                }
            }


            if (isUrl)
            {
                Debug.WriteLine($"[BrowserVM] Navigating directly to URL: {processedInput}");
                _navigationService.Navigate(processedInput);
            }
            else
            {
                Debug.WriteLine($"[BrowserVM] Treating input as search term: {processedInput}");
                PerformSteamWorkshopSearch(processedInput);
            }
        }

        private void ExecuteSearchCommand()
        {
            if (_disposed || string.IsNullOrWhiteSpace(SearchText)) return;
            PerformSteamWorkshopSearch(SearchText.Trim());
        }

        private void PerformSteamWorkshopSearch(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return;

            try
            {
                string encodedSearchText = WebUtility.UrlEncode(searchTerm);
                string searchUrl = $"https://steamcommunity.com/workshop/browse/?appid=294100&searchtext={encodedSearchText}&childpublishedfileid=0&browsesort=textsearch§ion=";

                Debug.WriteLine($"[BrowserVM] Performing search with URL: {searchUrl}");
                _navigationService.Navigate(searchUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserVM] Error constructing or executing search: {ex.Message}");
                RunOnUIThread(() => StatusChanged?.Invoke(this, $"Error performing search: {ex.Message}"));
            }
        }


        // --- Dispose Implementation (Adjusted) ---
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                Debug.WriteLine("[BrowserVM] Disposing derived managed resources...");
                // Unsubscribe from Navigation Service
                if (_navigationService != null)
                {
                    if (_navStatusHandler != null) _navigationService.StatusChanged -= _navStatusHandler;
                    if (_navStateChangedHandler != null) _navigationService.NavigationStateChanged -= _navStateChangedHandler;
                    if (_navModUrlValidityHandler != null) _navigationService.ModUrlValidityChanged -= _navModUrlValidityHandler;
                    if (_navCollectionUrlValidityHandler != null) _navigationService.CollectionUrlValidityChanged -= _navCollectionUrlValidityHandler; // Unsubscribe
                    if (_navSucceededAndValidHandler != null) _navigationService.NavigationSucceededAndUrlValid -= _navSucceededAndValidHandler;
                    if (_navSourceUrlChangedHandler != null) _navigationService.SourceUrlChanged -= _navSourceUrlChangedHandler;
                    if (_navStartedHandler != null) _navigationService.NavigationStarted -= _navStartedHandler;
                    if (_navEndedHandler != null) _navigationService.NavigationEnded -= _navEndedHandler;
                    Debug.WriteLine("[BrowserVM] Unsubscribed from NavigationService events.");
                }
                // Unsubscribe from Extractor Service
                if (_extractorService != null)
                {
                    // Note: The event name IsModInfoAvailableChanged is still used by the service itself
                    if (_extractorModInfoAvailableHandler != null) _extractorService.IsModInfoAvailableChanged -= _extractorModInfoAvailableHandler;
                    (_extractorService as IDisposable)?.Dispose();
                    _extractorService = null;
                    Debug.WriteLine("[BrowserVM] Unsubscribed from and disposed ModExtractorService.");
                }
                // Unsubscribe from Parent VM
                if (_parentViewModel != null && _parentPropertyChangedHandler != null)
                {
                    _parentViewModel.PropertyChanged -= _parentPropertyChangedHandler;
                    Debug.WriteLine("[BrowserVM] Unsubscribed from ParentViewModel PropertyChanged.");
                }
                // Clear handler references
                _navStatusHandler = null;
                _navStateChangedHandler = null;
                _navModUrlValidityHandler = null;
                _navCollectionUrlValidityHandler = null; // Clear ref
                _navSucceededAndValidHandler = null;
                _navSourceUrlChangedHandler = null;
                _extractorModInfoAvailableHandler = null;
                _parentPropertyChangedHandler = null;
                _navStartedHandler = null;
                _navEndedHandler = null;
                _webView = null;
                Debug.WriteLine("[BrowserVM] Dispose complete (derived managed resources).");
            }
            Debug.WriteLine($"[BrowserVM] Calling base.Dispose({disposing})...");
            base.Dispose(disposing);
            Debug.WriteLine($"[BrowserVM] Finished Dispose({disposing}). Base class disposed state: {_disposed}");
        }
        ~BrowserViewModel()
        {
            Debug.WriteLine("[BrowserVM] Finalizer called.");
            Dispose(false);
        }
    }
}