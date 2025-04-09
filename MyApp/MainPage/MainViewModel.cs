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
using System.Threading.Tasks; // Likely not needed if PathService handles config interaction

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
            // This event might be raised on a background thread from DownloaderVM.
            // RefreshDataAsync in ModsVM should be designed to handle this (e.g., use Dispatcher for UI updates).
            StatusMessage = "Download complete. Refreshing mod list...";
            Console.WriteLine("[MainViewModel] Received DownloadCompletedAndRefreshNeeded event. Triggering ModsVM refresh.");

            // Trigger the refresh logic. Calling the RefreshCommand ensures consistent behavior (status updates, etc.)
            // Ensure RefreshCommand's Execute method (RefreshData) eventually calls ModsVM.RefreshDataAsync()
            if (RefreshCommand.CanExecute(null))
            {
                // Execute the command. Consider if RefreshData does too much (like path reloads)
                // If RefreshData is heavy, call ModsVM refresh directly:
                _ = ModsVM?.RefreshDataAsync();
                StatusMessage = "Mod list refresh initiated."; // Update status after triggering
            }
            else
            {
                Console.WriteLine("[MainViewModel] RefreshCommand cannot execute, attempting direct refresh.");
                // Fallback to direct call if command can't execute for some reason
                _ = ModsVM?.RefreshDataAsync();
                StatusMessage = "Mod list refresh initiated (direct call).";
            }

            // Maybe add a small delay before clearing the status message
            Task.Delay(2000).ContinueWith(t => { if (StatusMessage == "Mod list refresh initiated." || StatusMessage == "Mod list refresh initiated (direct call).") StatusMessage = "Ready"; }, TaskScheduler.FromCurrentSynchronizationContext());
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
        private void RefreshData(object? parameter) // Make parameter nullable
        {
            StatusMessage = "Refreshing paths and mod data...";
            Console.WriteLine("[MainViewModel] RefreshData executing..."); // Debug log

            // 1. Refresh paths (Optional here if only mods changed, but good for consistency)
            _pathService.RefreshPaths();

            // 2. Update PathSettings properties from the refreshed service state
            string tempGame = _pathService.GetGamePath();
            string tempConfig = _pathService.GetConfigPath();
            string tempMods = _pathService.GetModsPath();
            string tempVersion = _pathService.GetGameVersion(tempGame);

            bool pathChanged = false;
            if (PathSettings.GamePath != tempGame) { PathSettings.GamePath = tempGame; pathChanged = true; }
            if (PathSettings.ConfigPath != tempConfig) { PathSettings.ConfigPath = tempConfig; pathChanged = true; }
            if (PathSettings.ModsPath != tempMods) { PathSettings.ModsPath = tempMods; pathChanged = true; }
            if (PathSettings.GameVersion != tempVersion) { PathSettings.GameVersion = tempVersion; } // Always update version display

            // 3. Trigger data refresh in the ModsViewModel
            // The ModsVM refresh should handle loading mods based on the *current* paths
            _ = ModsVM?.RefreshDataAsync(); // Fire and forget async call

            // 4. Update CanExecute state for commands dependent on paths
            if (pathChanged) // Only update if paths actually changed during THIS refresh
            {
                ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
            }

            // Clear status after a short delay
            Task.Delay(1500).ContinueWith(t => { if (StatusMessage == "Refreshing paths and mod data...") StatusMessage = "Refresh complete."; }, TaskScheduler.FromCurrentSynchronizationContext());
            Console.WriteLine("[MainViewModel] RefreshData finished."); // Debug log
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
