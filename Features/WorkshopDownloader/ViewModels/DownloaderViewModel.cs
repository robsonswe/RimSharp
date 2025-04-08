using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf; // <<< CHANGED
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.MyApp.AppFiles;

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    public class DownloaderViewModel : ViewModelBase
    {
        private readonly IWebNavigationService _navigationService;
        private readonly IDownloadQueueService _queueService;
        // Keep IModExtractorService interface, the implementation detail changes
        private IModExtractorService _extractorService;
        private string _statusMessage;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<DownloadItem> DownloadList => _queueService.Items;

        public bool CanGoBack => _navigationService.CanGoBack;
        public bool CanGoForward => _navigationService.CanGoForward;
        public bool IsValidModUrl => _navigationService.IsValidModUrl;

        // Browser Navigation Commands
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand GoHomeCommand { get; }

        // Other commands
        public ICommand AddModCommand { get; }
        public ICommand SetupSteamCmdCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand RemoveItemCommand { get; }

        public DownloaderViewModel(
            IWebNavigationService navigationService,
            IDownloadQueueService queueService)
        {
            _navigationService = navigationService;
            _queueService = queueService;

            // Set up event handlers
            _navigationService.StatusChanged += (s, message) => StatusMessage = message;
            _navigationService.NavigationStateChanged += (s, e) => {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
            };
            _navigationService.ModUrlValidityChanged += (s, valid) => {
                OnPropertyChanged(nameof(IsValidModUrl));
                // Need to raise CanExecuteChanged for commands depending on IsValidModUrl
                ((RelayCommand)AddModCommand).RaiseCanExecuteChanged();
            };

            _queueService.StatusChanged += (s, message) => StatusMessage = message;

            // Initialize commands
            GoBackCommand = new RelayCommand(_ => _navigationService.GoBack(), _ => CanGoBack);
            GoForwardCommand = new RelayCommand(_ => _navigationService.GoForward(), _ => CanGoForward);
            GoHomeCommand = new RelayCommand(_ => _navigationService.GoHome());

            AddModCommand = new RelayCommand(
                ExecuteAddModCommand,
                _ => IsValidModUrl); // Predicate based on property

            RemoveItemCommand = new RelayCommand(
                param => _queueService.RemoveFromQueue(param as DownloadItem));

            // Stub commands for future implementation
            SetupSteamCmdCommand = new RelayCommand(_ => { StatusMessage = "Setup SteamCMD: Not implemented"; });
            CheckUpdatesCommand = new RelayCommand(_ => { StatusMessage = "Check Updates: Not implemented"; });
            DownloadCommand = new RelayCommand(_ => { StatusMessage = "Download All: Not implemented"; });

             // Re-evaluate command states when navigation changes might affect them
            _navigationService.NavigationStateChanged += (s, e) =>
            {
                ((RelayCommand)GoBackCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GoForwardCommand).RaiseCanExecuteChanged();
            };
        }

        // Change the parameter type to the WPF WebView2
        public void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView) // <<< CHANGED
        {
            _navigationService.SetWebView(webView);
            // Pass the WPF WebView2 to the extractor service (ensure it's updated too)
            _extractorService = new ModExtractorService(webView);
        }

        private async void ExecuteAddModCommand(object parameter)
        {
             if (_extractorService == null)
             {
                 StatusMessage = "Error: Browser component not fully initialized.";
                 return;
             }
            try
            {
                StatusMessage = "Extracting mod info...";
                var modInfo = await _extractorService.ExtractFullModInfo();
                if (modInfo != null)
                {
                   if(!_queueService.AddToQueue(modInfo))
                   {
                       // Status message already set by QueueService if adding failed (e.g., duplicate)
                   }
                   // Status message set by QueueService on success too
                }
                else
                {
                     StatusMessage = "Could not extract mod info from the current page.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding mod: {ex.Message}";
                Console.WriteLine($"Error adding mod: {ex}"); // Log detailed error
            }
        }
    }
}
