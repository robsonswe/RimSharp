
#nullable enable
using System.Windows.Input;
using System.Windows.Forms; // Add reference to System.Windows.Forms assembly for FolderBrowserDialog
using System.IO; // For Directory.Exists
using System.Diagnostics;
using System;
using RimSharp.MyApp.AppFiles;
using RimSharp.Core.Commands;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Shared.Models;
using RimSharp.Infrastructure.Configuration;
using System.Threading.Tasks;
using RimSharp.Core.Extensions; // Likely not needed if PathService handles config interaction

namespace RimSharp.MyApp.MainPage
{
    public class MainViewModel : ViewModelBase
    {
        // Services needed DIRECTLY by MainViewModel
        private readonly IPathService _pathService;
        private readonly IConfigService _configService; // Needed for saving path settings
        private readonly IDialogService _dialogService;

        private string _selectedTab = "Mods"; // Default tab
        private ViewModelBase? _currentViewModel; // Holds the currently displayed module ViewModel

        // Properties for Module ViewModels (Injected)
        public ModsViewModel ModsVM { get; }
        public DownloaderViewModel? DownloaderVM { get; }

        // Application-wide settings or state
        public PathSettings PathSettings { get; }

        // Commands managed by MainViewModel
        public ICommand SwitchTabCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }


        // --- UPDATED CONSTRUCTOR ---
        // Only inject services used DIRECTLY by MainViewModel and the sub-viewmodels
        public MainViewModel(
            IPathService pathService,
            IConfigService configService,
            IDialogService dialogService,
            ModsViewModel modsViewModel,             // Inject ModsViewModel
            DownloaderViewModel downloaderViewModel  // Inject DownloaderViewModel
            )
        {
            _pathService = pathService;
            _configService = configService;
            _dialogService = dialogService;

            // --- Assign injected ViewModels ---
            ModsVM = modsViewModel;
            DownloaderVM = downloaderViewModel;
            // --- No longer creating ModsVM here ---
            if (DownloaderVM != null)
            {
                DownloaderVM.DownloadCompletedAndRefreshNeeded += DownloaderVM_DownloadCompletedAndRefreshNeeded;
            }

            // Initialize Path Settings (remains here as it's app-level config)
            PathSettings = new PathSettings
            {
                // Get initial values from the service
                GameVersion = _pathService.GetGameVersion(), // Get current version
                GamePath = _pathService.GetGamePath(),
                ConfigPath = _pathService.GetConfigPath(),
                ModsPath = _pathService.GetModsPath()
            };

            // Listen for changes in PathSettings if they need to trigger updates elsewhere
            PathSettings.PropertyChanged += PathSettings_PropertyChanged;

            // Set the initial view
            CurrentViewModel = ModsVM; // Start with the Mods view
            _selectedTab = "Mods"; // Explicitly set selected tab name

            // Initialize commands
            SwitchTabCommand = new RelayCommand(SwitchTab);
            BrowsePathCommand = new RelayCommand(BrowsePath, CanBrowsePath);
            SettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new RelayCommand(RefreshData);
            OpenFolderCommand = new RelayCommand(OpenFolder, CanOpenFolder);

            // Initial CanExecute check for OpenFolderCommand
            ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
        }
        // --- END UPDATED CONSTRUCTOR ---

        private void PathSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PathSettings));

            string? key = null;
            string? value = null;
            bool refreshNeeded = false;

            switch (e.PropertyName)
            {
                case nameof(PathSettings.GamePath):
                    key = "game_folder";
                    value = PathSettings.GamePath;
                    PathSettings.GameVersion = _pathService.GetGameVersion(value);
                    refreshNeeded = true;
                    break;
                case nameof(PathSettings.ConfigPath):
                    key = "config_folder";
                    value = PathSettings.ConfigPath;
                    refreshNeeded = true;
                    break;
                case nameof(PathSettings.ModsPath):
                    key = "mods_folder";
                    value = PathSettings.ModsPath;
                    refreshNeeded = true;
                    break;
            }

            if (key != null && value != null) // Ensure value is not null before saving
            {
                _configService.SetConfigValue(key, value);
                _configService.SaveConfig();

                if (refreshNeeded)
                {
                    // Use Task.Run for potentially long-running refresh, but ensure ModsVM handles UI updates correctly
                    // Calling the command ensures the status updates etc. happen too
                    if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
                    // Alternative: Direct call if RefreshCommand has other side effects you want to avoid here
                    // _ = ModsVM?.RefreshDataAsync();
                }
                ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
            }
        }


        private void DownloaderVM_DownloadCompletedAndRefreshNeeded(object? sender, EventArgs e)
        {
            StatusMessage = "Download complete. Refreshing mod list...";
            Console.WriteLine("[MainViewModel] Received DownloadCompletedAndRefreshNeeded event. Triggering RefreshCommand.");

            // Trigger the main refresh command, which handles path refresh AND mod refresh request
            if (RefreshCommand.CanExecute(null))
            {
                // Let the main command handle the coordinated refresh
                RefreshCommand.Execute(null);
                // Status message will be updated by RefreshData
            }
            else
            {
                // This case should be less likely now if RefreshCommand checks ModsVM.IsLoading
                StatusMessage = "Cannot start refresh: Another operation might be in progress.";
                Console.WriteLine("[MainViewModel] RefreshCommand cannot execute after download completion.");
                // Maybe attempt direct ModsVM refresh as fallback? Risky. Best to let the main command manage it.
                // _ = ModsVM.RequestRefreshCommand.Execute(null); // Less safe
            }
        }



        private void OpenFolder(object parameter)
        {
            var pathType = parameter as string;
            if (string.IsNullOrEmpty(pathType)) return;

            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                "ModsPath" => PathSettings.ModsPath,
                _ => null
            };

            // Check Directory.Exists before attempting to open
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                try
                {
                    // Use ProcessStartInfo for better control and error handling potential
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true // Important for opening folders
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError("Error Opening Folder", $"Could not open folder '{path}'.\nError: {ex.Message}");
                }
            }
            else
            {
                _dialogService.ShowWarning("Path Not Found", $"The path '{path ?? "specified"}' does not exist or is not set.");
            }
        }


        private bool CanOpenFolder(object parameter)
        {
            if (parameter is not string pathType) return false;

            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                "ModsPath" => PathSettings.ModsPath,
                _ => null
            };

            // Check for non-empty path AND directory existence
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }


        public ViewModelBase? CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public string SelectedTab
        {
            get => _selectedTab;
            private set => SetProperty(ref _selectedTab, value);
        }

        private void SwitchTab(object parameter)
        {
            var tabName = parameter as string;
            if (string.IsNullOrEmpty(tabName) || tabName == SelectedTab) return;

            ViewModelBase? nextViewModel = tabName switch
            {
                "Mods" => ModsVM,
                "Downloader" => DownloaderVM,
                _ => null
            };


            if (nextViewModel != null && CurrentViewModel != nextViewModel)
            {
                CurrentViewModel = nextViewModel;
                SelectedTab = tabName;
            }
        }

        private void BrowsePath(object? parameter)
        {
            var pathType = parameter as string;
            if (!CanBrowsePath(pathType)) return; // Use CanExecute logic

            using (var dialog = new FolderBrowserDialog())
            {
                if (pathType == null) return; // Add null check

                dialog.Description = $"Select RimWorld {pathType.Replace("Path", "")} Folder"; // User-friendly description
                string? currentPath = pathType switch
                {
                    "GamePath" => PathSettings.GamePath,
                    "ConfigPath" => PathSettings.ConfigPath,
                    "ModsPath" => PathSettings.ModsPath,
                    _ => null
                };

                // Set initial path if valid
                if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                {
                    dialog.SelectedPath = currentPath;
                }
                else // If path is invalid/unset, try a sensible default like UserProfile
                {
                    dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                dialog.ShowNewFolderButton = true; // Allow creating folders if needed

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    // Update the corresponding PathSettings property
                    // The PropertyChanged event handler on PathSettings will trigger saving and refresh
                    switch (pathType)
                    {
                        case "GamePath":
                            PathSettings.GamePath = selectedPath;
                            break;
                        case "ConfigPath":
                            PathSettings.ConfigPath = selectedPath;
                            break;
                        case "ModsPath":
                            PathSettings.ModsPath = selectedPath;
                            break;
                    }
                     // CanExecute for OpenFolder might change
                     ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private bool CanBrowsePath(object? parameter)
        {
            return parameter is string pathType &&
                   (pathType == "GamePath" || pathType == "ConfigPath" || pathType == "ModsPath");
        }

        private void OpenSettings(object parameter)
        {
            _dialogService.ShowInformation("Settings", "Settings dialog functionality is not yet implemented.");
        }

        // Centralized refresh logic
        private void RefreshData(object? parameter)
        {
            // Prevent refresh if ModsVM is busy (optional, ModsVM command handles this too)
            if (ModsVM.IsLoading)
            {
                StatusMessage = "Refresh skipped: Mod list operation already in progress.";
                Debug.WriteLine("[MainViewModel] RefreshData skipped, ModsVM is loading.");
                return;
            }

            StatusMessage = "Refreshing paths and mod data...";
            Debug.WriteLine("[MainViewModel] RefreshData executing...");

            try
            {
                // 1. Refresh paths FIRST
                _pathService.RefreshPaths();

                // 2. Update MainViewModel's PathSettings properties from the refreshed service state
                //    (Important: Do this BEFORE triggering mod load which might depend on these paths)
                string tempGame = _pathService.GetGamePath();
                string tempConfig = _pathService.GetConfigPath();
                string tempMods = _pathService.GetModsPath();
                string tempVersion = _pathService.GetGameVersion(tempGame); // Use refreshed game path

                bool pathChanged = false;
                // Use temporary variables to avoid triggering PropertyChanged multiple times if unchanged
                if (PathSettings.GamePath != tempGame) { PathSettings.GamePath = tempGame; pathChanged = true; }
                if (PathSettings.ConfigPath != tempConfig) { PathSettings.ConfigPath = tempConfig; pathChanged = true; }
                if (PathSettings.ModsPath != tempMods) { PathSettings.ModsPath = tempMods; pathChanged = true; }
                if (PathSettings.GameVersion != tempVersion) { PathSettings.GameVersion = tempVersion; pathChanged = true; } // Consider if version change alone needs CanExecute updates

                // 3. Trigger data refresh in the ModsViewModel via its command
                Debug.WriteLine("[MainViewModel] Requesting ModsViewModel refresh...");
                if (ModsVM.RequestRefreshCommand.CanExecute(null))
                {
                    // Execute the command. It handles its own async logic.
                    ModsVM.RequestRefreshCommand.Execute(null);
                }
                else
                {
                    StatusMessage = "Mod refresh request skipped: Mod list operation already in progress.";
                    Debug.WriteLine("[MainViewModel] ModsVM.RequestRefreshCommand cannot execute.");
                }

                // 4. Update CanExecute state for commands dependent on paths *if paths changed*
                if (pathChanged)
                {
                    // Use ThreadHelper to ensure UI thread execution
                    ThreadHelper.EnsureUiThread(() =>
                    {
                        ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
                        // Potentially other commands dependent on paths
                    });
                }

                // Status update handled within the command or after it finishes (more complex)
                // For simplicity, set status here and let ModsVM update it later if needed.
                StatusMessage = "Refresh initiated..."; // Or keep the previous message
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during refresh: {ex.Message}";
                Debug.WriteLine($"[MainViewModel] RefreshData Error: {ex}");
                _dialogService.ShowError("Refresh Error", $"An error occurred during refresh: {ex.Message}");
            }
            finally
            {
                // Clear status after a short delay or rely on ModsVM completion (tricky)
                Task.Delay(2500).ContinueWith(t => { if (StatusMessage == "Refresh initiated...") StatusMessage = "Refresh complete (check mod list)."; }, TaskScheduler.FromCurrentSynchronizationContext());
                Debug.WriteLine("[MainViewModel] RefreshData finished.");
            }
        }



        // Optional: Add a StatusMessage property if MainViewModel controls a status bar
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
    }
}
