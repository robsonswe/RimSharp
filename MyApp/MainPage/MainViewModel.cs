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
using RimSharp.Core.Extensions;
using RimSharp.Features.GitModManager.ViewModels;
using System.Threading;
using RimSharp.Core.Commands.Base; // For CancellationToken

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
        public GitModsViewModel GitModsVM { get; }


        // Application-wide settings or state
        public PathSettings PathSettings { get; }

        // Commands managed by MainViewModel
        public ICommand SwitchTabCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }

        // Optional: Add a StatusMessage property if MainViewModel controls a status bar
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }


        // --- UPDATED CONSTRUCTOR ---
        // Only inject services used DIRECTLY by MainViewModel and the sub-viewmodels
        public MainViewModel(
            IPathService pathService,
            IConfigService configService,
            IDialogService dialogService,
            ModsViewModel modsViewModel,             // Inject ModsViewModel
            DownloaderViewModel downloaderViewModel,  // Inject DownloaderViewModel
            GitModsViewModel gitModsViewModel)
        {
            _pathService = pathService;
            _configService = configService;
            _dialogService = dialogService;

            // --- Assign injected ViewModels ---
            ModsVM = modsViewModel;
            DownloaderVM = downloaderViewModel;
            GitModsVM = gitModsViewModel;

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

            // Initialize commands using base class helpers
            SwitchTabCommand = CreateCommand<string>(SwitchTab); // No CanExecute needed if it always runs
            BrowsePathCommand = CreateCommand<string>(BrowsePath, CanBrowsePath); // Typed parameter
            SettingsCommand = CreateCommand(OpenSettings); // No CanExecute needed
            RefreshCommand = CreateCommand(RefreshData, CanRefreshData); // Depends on ModsVM.IsLoading
            OpenFolderCommand = CreateCommand<string>(OpenFolder, CanOpenFolder); // Typed parameter, depends on PathSettings

            // Initial CanExecute check for OpenFolderCommand
            // No longer needed to call RaiseCanExecuteChanged manually here if dependencies are observed.
            // However, initial state check is good practice, and PropertyChanged triggers observation.
            // We observe PathSettings properties in PathSettings_PropertyChanged, which indirectly affects CanOpenFolder.
            // But RefreshCommand depends on ModsVM state, so we need to observe that.
            // Let's add observation for RefreshCommand explicitly.
            ((DelegateCommand)RefreshCommand).ObservesProperty(ModsVM, nameof(ModsViewModel.IsLoading));

        }
        // --- END UPDATED CONSTRUCTOR ---

        private void PathSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PathSettings)); // Notify UI about the whole object change

            string? key = null;
            string? value = null;
            bool refreshNeeded = false;
            bool pathSettingsChanged = false;

            switch (e.PropertyName)
            {
                case nameof(PathSettings.GamePath):
                    key = "game_folder";
                    value = PathSettings.GamePath;
                    // Update related property - this might trigger another PropertyChanged event
                    PathSettings.GameVersion = _pathService.GetGameVersion(value ?? string.Empty);
                    refreshNeeded = true;
                    pathSettingsChanged = true;
                    break;
                case nameof(PathSettings.ConfigPath):
                    key = "config_folder";
                    value = PathSettings.ConfigPath;
                    refreshNeeded = true;
                    pathSettingsChanged = true;
                    break;
                case nameof(PathSettings.ModsPath):
                    key = "mods_folder";
                    value = PathSettings.ModsPath;
                    refreshNeeded = true;
                    pathSettingsChanged = true;
                    break;
                case nameof(PathSettings.GameVersion):
                    // GameVersion change doesn't require saving config or full refresh,
                    // but might affect command states if they depend on it.
                    break;
            }

            if (key != null && value != null) // Ensure value is not null before saving
            {
                _configService.SetConfigValue(key, value);
                _configService.SaveConfig();
                // PathService cache is handled by RefreshPaths called in RefreshData if needed
            }

            if (refreshNeeded)
            {
                // Use Task.Run for potentially long-running refresh, but ensure ModsVM handles UI updates correctly
                // Calling the command ensures the status updates etc. happen too
                if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
            }

            if (pathSettingsChanged)
            {
                // Manual update still needed as PathSettings is a separate object,
                // not directly observed by the command using 'this'.
                // Alternatively, pass PathSettings properties to ObservesProperties.
                RunOnUIThread(() => ((DelegateCommand<string>)OpenFolderCommand).RaiseCanExecuteChanged());
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
            }
            else
            {
                StatusMessage = "Cannot start refresh: Another operation might be in progress.";
                Console.WriteLine("[MainViewModel] RefreshCommand cannot execute after download completion.");
            }
        }



        private void OpenFolder(string pathType) // Use specific type from CreateCommand<string>
        {
            // pathType is guaranteed non-null/empty if CanExecute passed, but check anyway
            if (string.IsNullOrEmpty(pathType)) return;

            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                "ModsPath" => PathSettings.ModsPath,
                _ => null
            };

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
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


        private bool CanOpenFolder(string pathType) // Use specific type
        {
            if (string.IsNullOrEmpty(pathType)) return false;

            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                "ModsPath" => PathSettings.ModsPath,
                _ => null
            };

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

        private void SwitchTab(string tabName) // Use specific type
        {
            if (string.IsNullOrEmpty(tabName)) return;

            ViewModelBase? nextViewModel = tabName switch
            {
                "Mods" => ModsVM,
                "Downloader" => DownloaderVM,
                "GitMods" => GitModsVM,
                _ => null
            };

            if (nextViewModel != null && CurrentViewModel != nextViewModel)
            {
                CurrentViewModel = nextViewModel;
                SelectedTab = tabName;
            }
        }


        private void BrowsePath(string pathType) // Use specific type
        {
            // CanExecute already checked by command infrastructure

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = $"Select RimWorld {pathType.Replace("Path", "")} Folder";
                string? currentPath = pathType switch
                {
                    "GamePath" => PathSettings.GamePath,
                    "ConfigPath" => PathSettings.ConfigPath,
                    "ModsPath" => PathSettings.ModsPath,
                    _ => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) // Sensible default if type unknown
                };

                if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                {
                    dialog.SelectedPath = currentPath;
                }
                else
                {
                    dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                dialog.ShowNewFolderButton = true;

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
                    // CanExecute for OpenFolder might change - handled by PathSettings_PropertyChanged
                }
            }
        }

        private bool CanBrowsePath(string pathType) // Use specific type
        {
            return !string.IsNullOrEmpty(pathType) &&
                   (pathType == "GamePath" || pathType == "ConfigPath" || pathType == "ModsPath");
        }

        private void OpenSettings() // Parameter removed as CreateCommand used parameterless overload
        {
            _dialogService.ShowInformation("Settings", "Settings dialog functionality is not yet implemented.");
        }

        private bool CanRefreshData()
        {
            // Depends on external VM state. Observation set up in constructor.
            return ModsVM != null && !ModsVM.IsLoading;
        }

        // Centralized refresh logic
        private void RefreshData() // Parameter removed
        {
            // CanExecute check already done by command framework.
            // Redundant check here doesn't hurt but isn't strictly necessary.
            // if (!CanRefreshData()) return; //

            StatusMessage = "Refreshing paths and mod data...";
            Debug.WriteLine("[MainViewModel] RefreshData executing...");

            bool pathsChanged = false;
            try
            {
                // 1. Refresh paths FIRST
                _pathService.RefreshPaths();

                // 2. Update MainViewModel's PathSettings properties from the refreshed service state
                string tempGame = _pathService.GetGamePath();
                string tempConfig = _pathService.GetConfigPath();
                string tempMods = _pathService.GetModsPath();
                string tempVersion = _pathService.GetGameVersion(tempGame);

                // Use temporary variables to avoid triggering PropertyChanged multiple times if unchanged
                // and set flag if any path actually changed
                if (PathSettings.GamePath != tempGame) { PathSettings.GamePath = tempGame; pathsChanged = true; }
                if (PathSettings.ConfigPath != tempConfig) { PathSettings.ConfigPath = tempConfig; pathsChanged = true; }
                if (PathSettings.ModsPath != tempMods) { PathSettings.ModsPath = tempMods; pathsChanged = true; }
                if (PathSettings.GameVersion != tempVersion) { PathSettings.GameVersion = tempVersion; pathsChanged = true; }

                // 3. Trigger data refresh in the ModsViewModel via its command
                Debug.WriteLine("[MainViewModel] Requesting ModsViewModel refresh...");
                if (ModsVM.RequestRefreshCommand.CanExecute(null))
                {
                    // Execute the command. Let it handle its own async logic and status updates.
                    ModsVM.RequestRefreshCommand.Execute(null);
                }
                else
                {
                    // This case should be rare now due to CanRefreshData check
                    StatusMessage = "Mod refresh request skipped: Mod list operation already in progress.";
                    Debug.WriteLine("[MainViewModel] ModsVM.RequestRefreshCommand cannot execute.");
                }

                // 4. Update CanExecute state for commands dependent on paths *if paths changed*
                // Manual update still needed as PathSettings is a separate object,
                // not directly observed by the command using 'this'.
                if (pathsChanged)
                {
                    RunOnUIThread(() => ((DelegateCommand<string>)OpenFolderCommand).RaiseCanExecuteChanged());
                }

                // Status update handled within the command or after it finishes.
                StatusMessage = "Refresh initiated...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during refresh: {ex.Message}";
                Debug.WriteLine($"[MainViewModel] RefreshData Error: {ex}");
                _dialogService.ShowError("Refresh Error", $"An error occurred during refresh: {ex.Message}");
            }
            finally
            {
                // Let ModsVM update status on completion. Maybe clear after timeout if ModsVM doesn't.
                Task.Delay(3500).ContinueWith(t => { if (StatusMessage == "Refresh initiated...") StatusMessage = "Refresh complete (check mod list)."; }, TaskScheduler.FromCurrentSynchronizationContext());
                Debug.WriteLine("[MainViewModel] RefreshData finished trigger.");
            }
        }
    }
}