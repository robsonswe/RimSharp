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
using RimSharp.Infrastructure.Configuration; // Likely not needed if PathService handles config interaction

namespace RimSharp.MyApp.MainPage
{
    public class MainViewModel : ViewModelBase
    {
        // Services needed DIRECTLY by MainViewModel
        private readonly IPathService _pathService;
        private readonly IConfigService _configService; // Needed for saving path settings
        private readonly IDialogService _dialogService;

        private string _selectedTab = "Mods"; // Default tab
        private ViewModelBase _currentViewModel; // Holds the currently displayed module ViewModel

        // Properties for Module ViewModels (Injected)
        public ModsViewModel ModsVM { get; }
        public DownloaderViewModel DownloaderVM { get; }

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
            PathSettings.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(PathSettings)); // Notify UI about PathSettings change

                // Save to config when paths change via UI
                string key = null;
                string value = null;
                bool refreshNeeded = false;

                switch (e.PropertyName)
                {
                    case nameof(PathSettings.GamePath):
                        key = "game_folder";
                        value = PathSettings.GamePath;
                        // Update derived setting
                        PathSettings.GameVersion = _pathService.GetGameVersion(value);
                        refreshNeeded = true; // Changing game path likely requires mod refresh
                        break;
                    case nameof(PathSettings.ConfigPath):
                        key = "config_folder";
                        value = PathSettings.ConfigPath;
                        refreshNeeded = true; // Changing config path requires reading new ModsConfig.xml
                        break;
                    case nameof(PathSettings.ModsPath):
                        key = "mods_folder";
                        value = PathSettings.ModsPath;
                        refreshNeeded = true; // Changing mods path requires mod refresh
                        break;
                }

                if (key != null)
                {
                    _configService.SetConfigValue(key, value);
                    _configService.SaveConfig(); // Consider debouncing saves if changes are frequent

                    // Trigger refresh if needed AFTER saving
                    if (refreshNeeded)
                    {
                        // Trigger the refresh logic (e.g., call RefreshData or specific refresh on ModsVM)
                        // Use Task.Run for long ops, ensure UI updates on UI thread.
                        // Using the RefreshCommand is a good way to centralize this.
                        if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
                    }
                    // Also notify CanExecuteChanged for commands depending on path validity
                    ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();
                }
            };

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


        private void OpenFolder(object parameter)
        {
            var pathType = parameter as string;
            if (string.IsNullOrEmpty(pathType)) return;

            string path = pathType switch
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

            string path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                "ModsPath" => PathSettings.ModsPath,
                _ => null
            };

            // Check for non-empty path AND directory existence
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }


        public ViewModelBase CurrentViewModel
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

            ViewModelBase nextViewModel = tabName switch
            {
                "Mods" => ModsVM,
                "Downloader" => DownloaderVM,
                _ => null // Handle unknown tab
            };

            if (nextViewModel != null && CurrentViewModel != nextViewModel)
            {
                CurrentViewModel = nextViewModel;
                SelectedTab = tabName;
            }
        }

        private void BrowsePath(object parameter)
        {
            var pathType = parameter as string;
            if (!CanBrowsePath(pathType)) return; // Use CanExecute logic

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = $"Select RimWorld {pathType.Replace("Path", "")} Folder"; // User-friendly description
                string currentPath = pathType switch
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

        private bool CanBrowsePath(object parameter)
        {
            return parameter is string pathType &&
                   (pathType == "GamePath" || pathType == "ConfigPath" || pathType == "ModsPath");
        }

        private void OpenSettings(object parameter)
        {
            _dialogService.ShowInformation("Settings", "Settings dialog functionality is not yet implemented.");
        }

        // Centralized refresh logic
        private void RefreshData(object parameter)
        {
            StatusMessage = "Refreshing paths and mod data..."; // Use StatusMessage if MainViewModel has one

            // 1. Refresh paths from config (in case config was edited externally)
            _pathService.RefreshPaths(); // Assuming PathService can refresh its internal state from ConfigService

            // 2. Update PathSettings properties from the refreshed service state
            // Use temporary variables to avoid excessive PropertyChanged triggers if values are the same
            string tempGame = _pathService.GetGamePath();
            string tempConfig = _pathService.GetConfigPath();
            string tempMods = _pathService.GetModsPath();
            string tempVersion = _pathService.GetGameVersion(tempGame); // Get version based on potentially new path

            // Assign properties. The PropertyChanged event will only fire if the value *actually* changes.
            // No need for the 'changed' flag here if subsequent actions always run.
            if (PathSettings.GamePath != tempGame) { PathSettings.GamePath = tempGame; }
            if (PathSettings.ConfigPath != tempConfig) { PathSettings.ConfigPath = tempConfig; }
            if (PathSettings.ModsPath != tempMods) { PathSettings.ModsPath = tempMods; }
            if (PathSettings.GameVersion != tempVersion) { PathSettings.GameVersion = tempVersion; } // Update version display

            // 3. Trigger data refresh in the relevant sub-viewmodel (ModsVM)
            // Use async void is acceptable for top-level command handlers if you handle loading state in ModsVM
            _ = ModsVM?.RefreshDataAsync();

            // 4. Update CanExecute state for commands dependent on paths
            ((RelayCommand)OpenFolderCommand).RaiseCanExecuteChanged();

            StatusMessage = "Refresh complete."; // Update status
            Console.WriteLine("RefreshData executed."); // Debug log
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
