#nullable enable
using System;
using System.ComponentModel;
using System.Windows.Input;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Threading.Tasks;
using System.Threading;
using System.Web;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;

namespace RimSharp.Features.WorkshopDownloader.Components.Browser
{
    public class BrowserViewModel : ViewModelBase, IDisposable
    {
        private readonly IWebNavigationService _navigationService;
        private readonly DownloaderViewModel _parentViewModel;
        private IBrowserControl? _browserControl;
        private IModExtractorService? _extractorService;
        private bool _isAnalyzingContent = false;

        private bool _canGoBack;
        public bool CanGoBack { get => _canGoBack; private set => this.RaiseAndSetIfChanged(ref _canGoBack, value); }

        private bool _canGoForward;
        public bool CanGoForward { get => _canGoForward; private set => this.RaiseAndSetIfChanged(ref _canGoForward, value); }

        private bool _isValidModUrl;
        public bool IsValidModUrl { get => _isValidModUrl; private set => this.RaiseAndSetIfChanged(ref _isValidModUrl, value); }

        private bool _isCollectionUrl;
        public bool IsCollectionUrl { get => _isCollectionUrl; private set => this.RaiseAndSetIfChanged(ref _isCollectionUrl, value); }

        private string? _currentPageSteamId;
        public string? CurrentPageSteamId { get => _currentPageSteamId; private set => this.RaiseAndSetIfChanged(ref _currentPageSteamId, value); }

        private string _actualCurrentUrl = string.Empty;
        public string ActualCurrentUrl { get => _actualCurrentUrl; private set => this.RaiseAndSetIfChanged(ref _actualCurrentUrl, value); }

        private const string HomeUrl = "https://steamcommunity.com/app/294100/workshop/";
        private string _addressBarUrl = HomeUrl;
        public string AddressBarUrl
        {
            get => _addressBarUrl;
            set
            {
                if (SetProperty(ref _addressBarUrl, value))
                {
                    OnPropertyChanged(nameof(AddressBarUrl));
                }
            }
        }

        public bool IsNavigating => _navigationService.IsNavigating;
        public bool IsDomReady => _navigationService.IsDomReady;

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }

        private bool _isSecure;
        public bool IsSecure { get => _isSecure; private set => this.RaiseAndSetIfChanged(ref _isSecure, value); }

        private string _searchText = string.Empty;
        public string SearchText { get => _searchText; set => this.RaiseAndSetIfChanged(ref _searchText, value); }

        private bool _isModInfoAvailable;
        public bool IsModInfoAvailable { get => _isModInfoAvailable; private set => this.RaiseAndSetIfChanged(ref _isModInfoAvailable, value); }

        private bool _isOperationInProgress;
        public bool IsOperationInProgress { get => _isOperationInProgress; private set => this.RaiseAndSetIfChanged(ref _isOperationInProgress, value); }

        private bool _isBrowserInitialized;
        public bool IsBrowserInitialized { get => _isBrowserInitialized; private set => this.RaiseAndSetIfChanged(ref _isBrowserInitialized, value); }

        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateToUrlCommand { get; }
        public ICommand SearchCommand { get; }

        public event EventHandler<string>? StatusChanged;

        public BrowserViewModel(IWebNavigationService navigationService, DownloaderViewModel parentViewModel)
        {
            _navigationService = navigationService;
            _parentViewModel = parentViewModel;

            _navigationService.SourceUrlChanged += NavigationService_SourceUrlChanged;
            _navigationService.StatusChanged += NavigationService_StatusChanged;
            _navigationService.NavigationStateChanged += NavigationService_NavigationStateChanged;
            _navigationService.NavigationStarted += NavigationService_NavigationStarted;
            _navigationService.NavigationEnded += NavigationService_NavigationEnded;
            _navigationService.PotentialWorkshopPageLoaded += NavigationService_PotentialWorkshopPageLoaded;
            _navigationService.DomContentLoaded += NavigationService_DomContentLoaded;

            _parentViewModel.PropertyChanged += ParentViewModel_PropertyChanged;

            GoBackCommand = CreateCommand(
                execute: () => _navigationService.GoBack(),
                canExecute: () => !IsOperationInProgress && CanGoBack && IsBrowserInitialized,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(CanGoBack), nameof(IsBrowserInitialized) });

            GoForwardCommand = CreateCommand(
                execute: () => _navigationService.GoForward(),
                canExecute: () => !IsOperationInProgress && CanGoForward && IsBrowserInitialized,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(CanGoForward), nameof(IsBrowserInitialized) });

            GoHomeCommand = CreateCommand(
                execute: () => _navigationService.GoHome(),
                canExecute: () => !IsOperationInProgress && IsBrowserInitialized,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(IsBrowserInitialized) });

            RefreshCommand = CreateCommand(
                execute: () => _browserControl?.Reload(),
                canExecute: () => !IsOperationInProgress && IsBrowserInitialized,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(IsBrowserInitialized) });

            NavigateToUrlCommand = CreateCommand<string>(
                execute: (url) => _navigationService.Navigate(url),
                canExecute: (url) => !IsOperationInProgress && !string.IsNullOrWhiteSpace(url) && IsBrowserInitialized,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(IsBrowserInitialized) });

            SearchCommand = CreateCommand(
                execute: ExecuteSearch,
                canExecute: () => !IsOperationInProgress && !string.IsNullOrWhiteSpace(SearchText) && IsBrowserInitialized,
                observedProperties: new[] { nameof(IsOperationInProgress), nameof(SearchText), nameof(IsBrowserInitialized) });
        }

        private void NavigationService_NavigationStarted(object? sender, string url)
        {
            if (url == "about:blank") return;
            Debug.WriteLine($"[BrowserVM] NavigationStarted: {url}");
            RunOnUIThread(() =>
            {
                IsLoading = true;
                AddressBarUrl = url;
                IsSecure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                IsValidModUrl = false;
                IsCollectionUrl = false;
                IsModInfoAvailable = false;
                _extractorService?.Reset();
                _isAnalyzingContent = false;
            });
        }

        private void NavigationService_NavigationEnded(object? sender, EventArgs e)
        {
            Debug.WriteLine("[BrowserVM] NavigationEnded");
            RunOnUIThread(() =>
            {
                IsLoading = false;
                if (_isAnalyzingContent)
                    _isAnalyzingContent = false;
            });
        }

        public void SetBrowserControl(IBrowserControl? browserControl)
        {
            if (_disposed) return;

            // Clean up existing control
            if (_extractorService != null)
            {
                _extractorService.IsModInfoAvailableChanged -= ExtractorService_IsModInfoAvailableChanged;
                (_extractorService as IDisposable)?.Dispose();
                _extractorService = null;
            }

            _browserControl = browserControl;

            if (browserControl == null)
            {
                
                _navigationService.SetBrowserControl(null);
                IsModInfoAvailable = false;
                CanGoBack = false;
                CanGoForward = false;
                IsBrowserInitialized = false;
                return;
            }
            IsBrowserInitialized = true;
            _extractorService = new ModExtractorService(browserControl);
            _extractorService.IsModInfoAvailableChanged += ExtractorService_IsModInfoAvailableChanged;
            IsModInfoAvailable = _extractorService.IsModInfoAvailable;

            _navigationService.SetBrowserControl(browserControl);

            RunOnUIThread(() =>
            {
                CanGoBack = _navigationService.CanGoBack;
                CanGoForward = _navigationService.CanGoForward;
            });
        }

        private void NavigationService_DomContentLoaded(object? sender, string url)
        {
            Debug.WriteLine($"[BrowserViewModel] DOMContentLoaded received for: {url}");
            RunOnUIThread(() => OnPropertyChanged(nameof(IsDomReady)));
            _ = AnalyzePageContentAsync();
        }

        private async Task AnalyzePageContentAsync()
        {
            if (_isAnalyzingContent || _extractorService == null) return;

            _isAnalyzingContent = true;
            Debug.WriteLine("[BrowserViewModel] Starting page content analysis...");

            try
            {
                // Reset previous state
                _extractorService.Reset();

                // Extract mod name, date, and Steam ID
                var nameTask = _extractorService.ExtractModName();
                var dateTask = _extractorService.ExtractModDateInfo();
                var idTask = ExtractSteamIdFromPageAsync();

                await Task.WhenAll(nameTask, dateTask, idTask);

                var name = nameTask.Result;
                var date = dateTask.Result;
                var id = idTask.Result;

                if (!string.IsNullOrEmpty(id))
                {
                    CurrentPageSteamId = id;
                }

                Debug.WriteLine($"[BrowserViewModel] Extraction complete - Name: '{name ?? "NULL"}', Date: '{date ?? "NULL"}', ID: '{id ?? "NULL"}'");
                Debug.WriteLine($"[BrowserViewModel] IsModInfoAvailable = {_extractorService.IsModInfoAvailable}");

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(date))
                {
                    Debug.WriteLine("[BrowserViewModel] Mod info is available!");
                }
                else
                {
                    Debug.WriteLine("[BrowserViewModel] Mod info not available - not a valid mod page?");
                    Debug.WriteLine($"[BrowserViewModel] Name is null/empty: {string.IsNullOrEmpty(name)}");
                    Debug.WriteLine($"[BrowserViewModel] Date is null/empty: {string.IsNullOrEmpty(date)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserViewModel] Error during page analysis: {ex}");
            }
            finally
            {
                _isAnalyzingContent = false;
            }
        }

        private void ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;
            string encodedSearch = WebUtility.UrlEncode(SearchText);
            string searchUrl = $"https://steamcommunity.com/workshop/browse/?appid=294100&searchtext={encodedSearch}&childpublishedfileid=0&browsemethod=textsearch&section=home";
            _navigationService.Navigate(searchUrl);
        }

        public Task<List<CollectionItemInfo>> ExtractCollectionItemsAsync(CancellationToken token = default)
        {
            return _extractorService?.ExtractCollectionItemsAsync() ?? Task.FromResult(new List<CollectionItemInfo>());
        }

        public Task<ModInfoDto?> GetCurrentModInfoAsync(CancellationToken token = default)
        {
            return _extractorService?.ExtractFullModInfo() ?? Task.FromResult<ModInfoDto?>(null);
        }

        public async Task<string?> ExtractSteamIdFromPageAsync(CancellationToken token = default)
        {
            if (_extractorService == null) return null;

            try
            {

                string script = @"(function() {

                    const subscribeBtn = document.querySelector('[data-modalcontrol]');
                    if (subscribeBtn) {
                        const modalControl = subscribeBtn.getAttribute('data-modalcontrol');
                        if (modalControl) {
                            const match = modalControl.match(/sharedfile_(\d+)/);
                            if (match) return match[1];
                        }
                    }
                    const links = document.querySelectorAll('a[href*=""filedetails""]');
                    for (const link of links) {
                        const href = link.href;
                        const urlParams = new URLSearchParams(new URL(href).search);
                        const id = urlParams.get('id');
                        if (id) return id;
                    }
                    const ogUrl = document.querySelector('meta[property=""og:url""]');
                    if (ogUrl) {
                        const content = ogUrl.getAttribute('content');
                        if (content) {
                            const urlParams = new URLSearchParams(new URL(content).search);
                            const id = urlParams.get('id');
                            if (id) return id;
                        }
                    }

                    return null;
                })();";

                string? result = await _extractorService.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(result) && result != "null")
                {
                    // Clean up the result (remove quotes if present)
                    result = result.Trim('"');
                    if (!string.IsNullOrEmpty(result) && long.TryParse(result, out _))
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserViewModel] Error extracting Steam ID from page: {ex.Message}");
            }

            return null;
        }

        private void ParentViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DownloaderViewModel.IsOperationInProgress))
            {
                IsOperationInProgress = _parentViewModel.IsOperationInProgress;
            }
        }

        private void NavigationService_StatusChanged(object? sender, string message) => StatusChanged?.Invoke(this, message);

        private void NavigationService_SourceUrlChanged(object? sender, string newUrl)
        {
            if (newUrl == "about:blank") return;
            Debug.WriteLine($"[BrowserVM] SourceUrlChanged: {newUrl}");
            RunOnUIThread(() =>
            {
                ActualCurrentUrl = newUrl;
                if (AddressBarUrl != newUrl && !IsNavigating)
                {
                    Debug.WriteLine($"[BrowserVM] Updating AddressBarUrl to: {newUrl}");
                    AddressBarUrl = newUrl;
                }
                IsSecure = newUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                // Early detection based on URL pattern
                if (Uri.TryCreate(newUrl, UriKind.Absolute, out var uri))
                {
                    bool isSteamWorkshop = uri.Host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase);
                    if (isSteamWorkshop)
                    {
                        var query = HttpUtility.ParseQueryString(uri.Query);
                        string? id = query.Get("id");
                        CurrentPageSteamId = id;

                        bool isModPage = uri.AbsolutePath.Contains("/filedetails/", StringComparison.OrdinalIgnoreCase);
                        bool isCollectionPage = uri.AbsolutePath.Contains("/workshop/browse/", StringComparison.OrdinalIgnoreCase) && 
                                               (uri.AbsolutePath.Contains("/collections/", StringComparison.OrdinalIgnoreCase) || 
                                                query.Get("section") == "collections");

                        IsValidModUrl = isModPage && !string.IsNullOrEmpty(id);
                        IsCollectionUrl = isCollectionPage;
                    }
                }
            });
        }

        private void NavigationService_NavigationStateChanged(object? sender, EventArgs e)
        {
            RunOnUIThread(() =>
            {
                CanGoBack = _navigationService.CanGoBack;
                CanGoForward = _navigationService.CanGoForward;
            });
        }

        private void ExtractorService_IsModInfoAvailableChanged(object? sender, EventArgs e)
        {
            if (_extractorService != null) IsModInfoAvailable = _extractorService.IsModInfoAvailable;
        }

        private async void NavigationService_PotentialWorkshopPageLoaded(object? sender, string url)
        {
            if (_disposed || _extractorService == null || _isAnalyzingContent) return;
            if ((IsValidModUrl && IsModInfoAvailable) || IsCollectionUrl) return;

            _isAnalyzingContent = true;
            StatusChanged?.Invoke(this, "Analyzing page content...");

            var collectionItems = await _extractorService.ExtractCollectionItemsAsync();
            bool isCollection = collectionItems != null && collectionItems.Any();

            if (isCollection)
            {
                IsCollectionUrl = true;
                IsValidModUrl = false;
                IsModInfoAvailable = false;
                StatusChanged?.Invoke(this, $"Collection detected ({collectionItems!.Count} items).");
            }
            else
            {
                var modInfo = await _extractorService.ExtractFullModInfo();
                if (modInfo != null)
                {
                    IsValidModUrl = true;
                    IsCollectionUrl = false;
                    IsModInfoAvailable = true;
                    StatusChanged?.Invoke(this, "Mod details detected.");
                }
            }
            _isAnalyzingContent = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _navigationService.SourceUrlChanged -= NavigationService_SourceUrlChanged;
                    _navigationService.StatusChanged -= NavigationService_StatusChanged;
                    _navigationService.NavigationStateChanged -= NavigationService_NavigationStateChanged;
                    _navigationService.NavigationStarted -= NavigationService_NavigationStarted;
                    _navigationService.NavigationEnded -= NavigationService_NavigationEnded;
                    _navigationService.PotentialWorkshopPageLoaded -= NavigationService_PotentialWorkshopPageLoaded;
                    _parentViewModel.PropertyChanged -= ParentViewModel_PropertyChanged;

                    if (_extractorService != null)
                    {
                        _extractorService.IsModInfoAvailableChanged -= ExtractorService_IsModInfoAvailableChanged;
                        (_extractorService as IDisposable)?.Dispose();
                    }
                }

                base.Dispose(disposing);
            }
        }
    }
}


