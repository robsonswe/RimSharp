using RimSharp.Models;
using RimSharp.Services;
using RimSharp.Handlers;
using RimSharp.ViewModels.Modules.Mods;       // Import module VM namespace
using RimSharp.ViewModels.Modules.Downloader; // Import module VM namespace
using System.Windows.Input;
// Remove Microsoft.Win32 if not used directly here
using System.Windows.Forms; // Add reference to System.Windows.Forms assembly for FolderBrowserDialog
using System.IO; // For Directory.Exists

namespace RimSharp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // Keep services needed by MainViewModel or to pass to module ViewModels
        private readonly IPathService _pathService;
        private readonly IModService _modService; // Keep to pass to ModsViewModel

        private string _selectedTab = "Mods"; // Default tab
        private ViewModelBase _currentViewModel; // Holds the currently displayed module ViewModel

        // Properties for Module ViewModels
        public ModsViewModel ModsVM { get; }
        public DownloaderViewModel DownloaderVM { get; }

        // Application-wide settings or state
        public PathSettings PathSettings { get; }

        // Commands managed by MainViewModel
        public ICommand SwitchTabCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand RefreshCommand { get; }

        // Constructor: Instantiate module VMs and set initial state
        public MainViewModel(IModService modService, IPathService pathService)
        {
            _modService = modService;
            _pathService = pathService;

            // Initialize Path Settings (remains here as it's app-level config)
            PathSettings = new PathSettings
            {
                GameVersion = _pathService.GetGameVersion(), // Initialize with detected values
                GamePath = _pathService.GetGamePath(),
                ConfigPath = _pathService.GetConfigPath(),
                ModsPath = _pathService.GetModsPath()
            };
            // Listen for changes in PathSettings if they need to trigger updates elsewhere
            PathSettings.PropertyChanged += (s, e) => OnPropertyChanged(nameof(PathSettings));


            // Create instances of the module ViewModels, passing dependencies
            ModsVM = new ModsViewModel(_modService);
            DownloaderVM = new DownloaderViewModel(/* Pass services if needed */);

            // Set the initial view
            CurrentViewModel = ModsVM; // Start with the Mods view
            _selectedTab = "Mods"; // Explicitly set selected tab name

            // Initialize commands
            SwitchTabCommand = new RelayCommand(SwitchTab);
            BrowsePathCommand = new RelayCommand(BrowsePath, CanBrowsePath); // Add CanExecute
            SettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new RelayCommand(RefreshData);

        }

        // Property for the currently active module ViewModel - PUBLIC SETTER
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value); // Use public setter
        }

        // Property to track the selected tab name (for button styling, etc.)
        public string SelectedTab
        {
            get => _selectedTab;
            // Setter should be private if only changed via SwitchTab command
            private set => SetProperty(ref _selectedTab, value);
        }

        // Command implementation for switching tabs/modules
        private void SwitchTab(object parameter)
        {
            var tabName = parameter as string;
            if (string.IsNullOrEmpty(tabName) || tabName == SelectedTab) return; // No change or invalid parameter

            ViewModelBase nextViewModel = null;
            switch (tabName)
            {
                case "Mods":
                    nextViewModel = ModsVM;
                    break;
                case "Downloader":
                    nextViewModel = DownloaderVM;
                    break;
                // Add cases for other tabs/modules here
                default:
                    // Optional: handle unknown tab name, maybe log an error
                    return; // Don't change tab if unknown
            }

            // Only update if the ViewModel actually changed (might be same instance)
            if (CurrentViewModel != nextViewModel)
            {
                CurrentViewModel = nextViewModel;
            }
            SelectedTab = tabName; // Update the selected tab name state
        }

        // Command implementation for browsing paths
        private void BrowsePath(object parameter)
        {
            var pathType = parameter as string;
            if (string.IsNullOrEmpty(pathType)) return;

            using (var dialog = new FolderBrowserDialog()) // Ensure dialog is disposed
            {
                dialog.Description = $"Select {pathType} Folder";
                string currentPath = pathType switch
                {
                    "GamePath" => PathSettings.GamePath,
                    "ConfigPath" => PathSettings.ConfigPath,
                    "ModsPath" => PathSettings.ModsPath,
                    _ => null
                };

                if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                {
                    dialog.SelectedPath = currentPath;
                }

                // Ensure you are running on a thread that can show dialogs (usually UI thread)
                // In WPF, commands are typically executed on the UI thread unless explicitly backgrounded
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    switch (pathType)
                    {
                        case "GamePath":
                            PathSettings.GamePath = selectedPath;
                            // Update dependent properties like game version
                            PathSettings.GameVersion = _pathService.GetGameVersion(selectedPath);
                            break;
                        case "ConfigPath":
                            PathSettings.ConfigPath = selectedPath;
                            // Potentially trigger config reload or validation
                            break;
                        case "ModsPath":
                            PathSettings.ModsPath = selectedPath;
                            // IMPORTANT: Refresh mods list
                            // Use Task.Run for potentially long operations, but ensure UI updates happen on UI thread
                            // Or call async method directly if service/VM handles threading
                            _ = ModsVM?.RefreshDataAsync(); // Call refresh method on ModsVM (fire-and-forget is okay here if UI handles IsLoading)
                            break;
                    }
                }
            } // Dialog is disposed here
        }

        private bool CanBrowsePath(object parameter)
        {
            // Enable browsing for recognized path types
            return parameter is string pathType &&
                   (pathType == "GamePath" || pathType == "ConfigPath" || pathType == "ModsPath");
        }

        private void OpenSettings(object parameter)
        {
            // TODO: Implement settings dialog opening logic
            System.Windows.MessageBox.Show("Settings dialog will open here");
        }

        private void RefreshData(object parameter)
        {
            // Refresh mods data
            _ = ModsVM?.RefreshDataAsync();
        }


        // Note: IsLoading is now managed within ModsViewModel
        // Note: Mod selection properties (SelectedMod) and lists (Active/InactiveMods) are in ModsViewModel
        // Note: SelectModCommand is in ModsViewModel
        // Note: LoadDataAsync / LoadDummyData are in ModsViewModel
    }
}