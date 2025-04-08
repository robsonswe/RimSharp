using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf; // <<< CHANGED
using RimSharp.Core.Commands;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.MyApp.AppFiles;
using RimSharp.Shared.Services.Contracts; // Add this for IModService, IDialogService
using System.Net.Http; // Required if injecting HttpClient directly, better via factory if possible
using System.Collections.Generic; // Required for List
using System.Threading.Tasks;
using System.Linq;
using RimSharp.Shared.Models;
using RimSharp.MyApp.Dialogs;
using System.Threading; // Required for Task

namespace RimSharp.Features.WorkshopDownloader.ViewModels
{
    public class DownloaderViewModel : ViewModelBase
    {
        private readonly IWebNavigationService _navigationService;
        private readonly IDownloadQueueService _queueService;
        // Keep IModExtractorService interface, the implementation detail changes
        private IModExtractorService _extractorService;
        private string _statusMessage;

        private readonly IModService _modService; // To get the list of installed mods
        private readonly IDialogService _dialogService; // To show the update check dialog
        private readonly IWorkshopUpdateCheckerService _updateCheckerService; // To perform the update check
        private readonly ISteamCmdService _steamCmdService;

        private Microsoft.Web.WebView2.Wpf.WebView2 _webView;
        private CancellationTokenSource _currentOperationCts; 

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<DownloadItem> DownloadList => _queueService.Items;

        public bool CanGoBack => _navigationService.CanGoBack;
        public bool CanGoForward => _navigationService.CanGoForward;
        public bool IsValidModUrl => _navigationService.IsValidModUrl;
        public bool IsSteamCmdReady => _steamCmdService?.IsSetupComplete ?? false; 

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
        public ICommand NavigateToUrlCommand { get; }
        public ICommand CancelOperationCommand { get; }

        public DownloaderViewModel(
    IWebNavigationService navigationService,
    IDownloadQueueService queueService,
    IModService modService, // Add IModService
    IDialogService dialogService, // Add IDialogService
    IWorkshopUpdateCheckerService updateCheckerService, // Add IWorkshopUpdateCheckerService
    ISteamCmdService steamCmdService)
        {
            _navigationService = navigationService;
            _queueService = queueService;
            _modService = modService; // Store injected service
            _dialogService = dialogService; // Store injected service
            _updateCheckerService = updateCheckerService; // Store injected service
            _steamCmdService = steamCmdService;

            // Set up event handlers
            _navigationService.StatusChanged += (s, message) => StatusMessage = message;
            _navigationService.NavigationStateChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
            };
            _navigationService.ModUrlValidityChanged += (s, valid) =>
            {
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
            CheckUpdatesCommand = new RelayCommand(async _ => await ExecuteCheckUpdatesCommand(), CanExecuteCheckUpdates);
            NavigateToUrlCommand = new RelayCommand(ExecuteNavigateToUrlCommand);

            // Stub commands for future implementation
            SetupSteamCmdCommand = new RelayCommand(async _ => await ExecuteSetupSteamCmdCommand(), CanExecuteSetupSteamCmd);
            DownloadCommand = new RelayCommand(async _ => await ExecuteDownloadCommand(), CanExecuteDownload);
            CancelOperationCommand = new RelayCommand(ExecuteCancelOperation, CanExecuteCancelOperation); // <<< NEW

            _queueService.Items.CollectionChanged += (s, e) => ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged(); // Initial check

            // Re-evaluate command states when navigation changes might affect them
            _navigationService.NavigationStateChanged += (s, e) =>
            {
                ((RelayCommand)GoBackCommand).RaiseCanExecuteChanged();
                ((RelayCommand)GoForwardCommand).RaiseCanExecuteChanged();
            };
        }



        // Change the parameter type to the WPF WebView2
public void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
{
    _webView = webView;
    _navigationService.SetWebView(webView);
    _extractorService = new ModExtractorService(webView);
    ((RelayCommand)CheckUpdatesCommand).RaiseCanExecuteChanged();
}
        private async Task UpdateSteamCmdReadyStatus()
        {
             await _steamCmdService.CheckSetupAsync(); // Ensure service state is fresh
             RunOnUIThread(() => {
                OnPropertyChanged(nameof(IsSteamCmdReady));
                // Re-evaluate commands that depend on SteamCMD status
                ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
             });
        }

        private bool CanExecuteSetupSteamCmd(object parameter)
        {
            // Can always try to set up, unless an operation is already running
            return _currentOperationCts == null;
        }

        private async Task ExecuteSetupSteamCmdCommand()
        {
            if (_currentOperationCts != null) return; // Prevent concurrent operations

            ProgressDialogViewModel? progressDialog = null;
            _currentOperationCts = new CancellationTokenSource();
            ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged(); // Disable setup btn
            ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged(); // Disable download btn
            ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged(); // Enable cancel btn


            try
            {
                progressDialog = _dialogService.ShowProgressDialog(
                    "SteamCMD Setup",
                    "Starting setup...",
                    true); // Can Cancel

                 // Link progress dialog cancellation to our token
                 progressDialog.Cancelled += (s, e) => _currentOperationCts?.Cancel();

                 var progressReporter = new Progress<string>(message =>
                 {
                     if (progressDialog != null) progressDialog.Message = message;
                     StatusMessage = message; // Update main status bar too
                 });

                 StatusMessage = "Running SteamCMD Setup...";
                 bool success = await _steamCmdService.SetupAsync(progressReporter, _currentOperationCts.Token);

                 if (success)
                 {
                     progressDialog?.Complete("Setup completed successfully!");
                     StatusMessage = "SteamCMD setup successful.";
                     _dialogService.ShowInformation("Setup Complete", "SteamCMD has been set up successfully.");
                 }
                 else if (!_currentOperationCts.IsCancellationRequested)
                 {
                      // Failure message already shown by service or progress reporter
                     progressDialog?.Cancel("Setup failed. See previous messages for details.");
                     StatusMessage = "SteamCMD setup failed.";
                 } else {
                      // Cancellation message
                     progressDialog?.Cancel("Setup cancelled by user.");
                     StatusMessage = "SteamCMD setup cancelled.";
                 }
            }
            catch (OperationCanceledException)
            {
                 progressDialog?.Cancel("Setup cancelled by user.");
                 StatusMessage = "SteamCMD setup cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during SteamCMD setup: {ex.Message}";
                progressDialog?.Cancel($"Error: {ex.Message}");
                _dialogService.ShowError("Setup Error", $"An unexpected error occurred during setup: {ex.Message}");
            }
            finally
            {
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                 await UpdateSteamCmdReadyStatus(); // Refresh IsSteamCmdReady property
                 RunOnUIThread(() => { // Re-enable buttons
                    ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
                 });
                 progressDialog?.ForceClose(); // Ensure dialog closes if not already
            }
        }


        private bool CanExecuteDownload(object parameter)
        {
            // Need items in the queue AND SteamCMD setup AND no operation running
            return DownloadList.Any() && IsSteamCmdReady && _currentOperationCts == null;
        }

        private async Task ExecuteDownloadCommand()
        {
             if (_currentOperationCts != null) return; // Prevent concurrent operations

            if (!IsSteamCmdReady)
            {
                var result = _dialogService.ShowConfirmation("SteamCMD Not Ready", "SteamCMD setup is not detected or incomplete. Do you want to run the setup now?", showCancel: true);
                if (result == MessageDialogResult.OK || result == MessageDialogResult.Yes) // Assuming OK is the positive action for confirmation
                {
                    await ExecuteSetupSteamCmdCommand();
                    // If setup was successful, try download again? Or just let user click again.
                    // Let user click again for simplicity.
                    return;
                }
                else
                {
                    StatusMessage = "Download cancelled: SteamCMD setup required.";
                    return;
                }
            }

            var idsToDownload = DownloadList.Select(item => item.SteamId).ToList();
            if (!idsToDownload.Any())
            {
                StatusMessage = "Download queue is empty.";
                return;
            }

            ProgressDialogViewModel? progressDialog = null;
            _currentOperationCts = new CancellationTokenSource();
             ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged(); // Disable setup btn
            ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged(); // Disable download btn
            ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged(); // Enable cancel btn


            try
            {
                 progressDialog = _dialogService.ShowProgressDialog(
                    "Downloading Mods",
                    $"Preparing to download {idsToDownload.Count} mod(s)...",
                    true); // Can Cancel

                 progressDialog.Cancelled += (s, e) => _currentOperationCts?.Cancel();

                 var progressReporter = new Progress<string>(message =>
                 {
                     if (progressDialog != null) progressDialog.Message = message;
                     StatusMessage = message; // Update main status bar too
                 });

                 StatusMessage = $"Downloading {idsToDownload.Count} mod(s) via SteamCMD...";
                 bool success = await _steamCmdService.DownloadModsAsync(idsToDownload, false, progressReporter, _currentOperationCts.Token);

                 if (success)
                 {
                     progressDialog?.Complete("Downloads completed successfully!");
                     StatusMessage = "Mod downloads finished.";
                      _dialogService.ShowInformation("Download Complete", $"Successfully processed {idsToDownload.Count} mod(s) with SteamCMD.");
                     // Clear queue on success
                     RunOnUIThread(() => {
                         // Create a copy to avoid modification issues while iterating/clearing
                         var itemsToRemove = DownloadList.ToList();
                         foreach(var item in itemsToRemove) {
                             _queueService.RemoveFromQueue(item);
                         }
                     });
                 }
                 else if (!_currentOperationCts.IsCancellationRequested)
                 {
                      // Failure message already shown by service or progress reporter
                     progressDialog?.Cancel("Download failed. Check SteamCMD output for details.");
                     StatusMessage = "Mod download failed. See log/output.";
                      _dialogService.ShowWarning("Download Failed", "SteamCMD reported errors during the download process. Check the output in the progress dialog or status messages for details.");
                 } else {
                      // Cancellation message
                     progressDialog?.Cancel("Download cancelled by user.");
                     StatusMessage = "Mod download cancelled.";
                 }
            }
            catch (OperationCanceledException)
            {
                 progressDialog?.Cancel("Download cancelled by user.");
                 StatusMessage = "Mod download cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during download: {ex.Message}";
                progressDialog?.Cancel($"Error: {ex.Message}");
                _dialogService.ShowError("Download Error", $"An unexpected error occurred during download: {ex.Message}");
            }
            finally
            {
                _currentOperationCts?.Dispose();
                _currentOperationCts = null;
                 RunOnUIThread(() => { // Re-enable buttons
                    ((RelayCommand)SetupSteamCmdCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
                 });
                  progressDialog?.ForceClose(); // Ensure dialog closes
            }
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
                    if (!_queueService.AddToQueue(modInfo)) { /* Status set by queue service */ }
                }
                else
                {
                    StatusMessage = "Could not extract mod info from the current page.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding mod: {ex.Message}";
                Console.WriteLine($"Error adding mod: {ex}");
            }
        }

        private void ExecuteNavigateToUrlCommand(object url)
{
        Console.WriteLine($"Command received: {url} (type: {url?.GetType().Name})");

    if (url is string urlString && !string.IsNullOrEmpty(urlString))
    {
        try
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate(urlString);
            }
            else if (_webView != null)
            {
                _webView.Source = new Uri(urlString);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error navigating to URL: {ex.Message}";
        }
    }
}

        private bool CanExecuteCheckUpdates(object parameter)
        {
            // Enable if the mod service has loaded some mods
            // More specific: enable if there are workshop mods loaded
            try
            {
                return _modService?.GetLoadedMods()
                                  .Any(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _))
                       ?? false;
            }
            catch
            {
                return false; // Handle potential errors during GetLoadedMods
            }
        }


        private async Task ExecuteCheckUpdatesCommand()
{
    StatusMessage = "Gathering installed workshop mods...";
    List<ModItem> workshopMods;
    try
    {
        // Get all workshop mods with valid Steam IDs
        workshopMods = _modService.GetLoadedMods()
           .Where(m => !string.IsNullOrEmpty(m.SteamId) && long.TryParse(m.SteamId, out _))
           .OrderBy(m => m.Name)
           .ToList();
    }
    catch (Exception ex)
    {
        StatusMessage = $"Error loading mod list: {ex.Message}";
        Console.WriteLine($"Error getting mods for update check: {ex}");
        return;
    }

    if (!workshopMods.Any())
    {
        StatusMessage = "No installed Steam Workshop mods found to check.";
        _dialogService.ShowInformation("Check Updates", "No installed Steam Workshop mods were found in your mods folder.");
        return;
    }

    // Create and show the selection dialog
    var dialogViewModel = new UpdateCheckDialogViewModel(workshopMods);
    var dialogResult = _dialogService.ShowUpdateCheckDialog(dialogViewModel);

    if (dialogResult != UpdateCheckDialogResult.CheckUpdates)
    {
        StatusMessage = "Update check cancelled.";
        return;
    }

    var selectedMods = dialogViewModel.GetSelectedMods().ToList();
    if (!selectedMods.Any())
    {
        StatusMessage = "No mods were selected for update check.";
        return;
    }

    StatusMessage = $"Checking {selectedMods.Count} mod(s) for updates...";
    
    // Create and show progress dialog
    ProgressDialogViewModel progressDialog = null;
    IProgress<(int current, int total, string modName)> progress = null;
    
    try
    {
        progressDialog = _dialogService.ShowProgressDialog(
            "Checking for Updates", 
            "Preparing to check mods...",
            true);

        progress = new Progress<(int current, int total, string modName)>(update =>
        {
            progressDialog.Message = $"Checking {update.modName}... ({update.current}/{update.total})";
            progressDialog.Progress = (int)((double)update.current / update.total * 100);
            
            // Update status message in main window too
            StatusMessage = $"Checking {update.modName} ({update.current} of {update.total})...";
        });

        // Perform the update check
        var updateResult = await _updateCheckerService.CheckForUpdatesAsync(selectedMods, progress);

        // Close progress dialog
        progressDialog.Complete();
        progressDialog = null;

        // Report results
        string summary = $"Update check complete. Checked: {updateResult.ModsChecked}. Updates found: {updateResult.UpdatesFound}.";
        if (updateResult.ErrorsEncountered > 0)
        {
            summary += $" Errors: {updateResult.ErrorsEncountered}.";
            
            // Show first 3 errors if there are many
            var errorSample = updateResult.ErrorMessages.Take(3).ToList();
            var errorMessage = string.Join("\n", errorSample);
            
            if (updateResult.ErrorMessages.Count > 3)
            {
                errorMessage += $"\n(and {updateResult.ErrorsEncountered - 3} more errors...)";
            }
            
            _dialogService.ShowWarning("Update Check Errors", 
                $"Encountered {updateResult.ErrorsEncountered} error(s) during the update check.\n\n{errorMessage}");
        }

        StatusMessage = summary;
        
        if (updateResult.UpdatesFound > 0)
        {
            _dialogService.ShowInformation("Updates Found", 
                $"Found {updateResult.UpdatesFound} mod(s) with updates available. They have been added to the download queue.");
        }
        else
        {
            _dialogService.ShowInformation("No Updates Found", 
                "All selected mods are up to date.");
        }
    }
    catch (OperationCanceledException)
    {
        // User cancelled the operation
        progressDialog?.Cancel();
        StatusMessage = "Update check was cancelled.";
        _dialogService.ShowInformation("Cancelled", "The update check was cancelled.");
    }
    catch (Exception ex)
    {
        progressDialog?.Cancel();
        StatusMessage = $"An error occurred during the update check process: {ex.Message}";
        Console.WriteLine($"Error executing update check: {ex}");
        _dialogService.ShowError("Update Check Failed", $"An unexpected error occurred: {ex.Message}");
    }
    finally
    {
        progressDialog?.Cancel();
    }
}

        private bool CanExecuteCancelOperation(object parameter)
        {
            return _currentOperationCts != null && !_currentOperationCts.IsCancellationRequested;
        }

        private void ExecuteCancelOperation(object parameter)
        {
            if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
            {
                StatusMessage = "Attempting to cancel operation...";
                _currentOperationCts.Cancel();
                RunOnUIThread(() => ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged()); // Disable cancel button immediately
            }
        }


    }
}
