#nullable enable
using System.Windows.Input;
using System.Windows.Forms; // Add reference to System.Windows.Forms assembly for FolderBrowserDialog
using System.IO; // For Directory.Exists, Path.Combine, Path.GetPathRoot
using System.Diagnostics;
using System;
using System.ComponentModel;
using RimSharp.AppDir.AppFiles;
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
using System.Collections.Generic;
using RimSharp.Features.VramAnalysis.ViewModels;

namespace RimSharp.AppDir.MainPage
{
    public class MainViewModel : ViewModelBase
    {
        // Services needed DIRECTLY by MainViewModel
        private readonly IPathService _pathService;
        private readonly IConfigService _configService; // Needed for saving path settings
        private readonly IDialogService _dialogService;
        private readonly IApplicationNavigationService _navigationService;
        private readonly IUpdaterService _updaterService;

        private string _selectedTab = "Mods"; // Default tab
        private INotifyPropertyChanged? _currentViewModel; // Holds the currently displayed module ViewModel

        // Properties for Module ViewModels (Injected)
        public IModsViewModel ModsVM { get; } = null!;
        public IDownloaderViewModel? DownloaderVM { get; }
        public IGitModsViewModel GitModsVM { get; } = null!;

        public IVramAnalysisViewModel VramAnalysisVM { get; } = null!;

        // Application-wide settings or state
        public PathSettings PathSettings { get; } = null!;

        // Task to track path initialization
        private Task? _pathInitializationTask;

        // Commands managed by MainViewModel
        public ICommand SwitchTabCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand AboutCommand { get; }

        // Optional: Add a StatusMessage property if MainViewModel controls a status bar
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

        // --- Properties for Update Notification ---
        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        private string _updateTooltipText = string.Empty;
        public string UpdateTooltipText
        {
            get => _updateTooltipText;
            set => SetProperty(ref _updateTooltipText, value);
        }

        private bool _isInitialLoading = true;
        public bool IsInitialLoading
        {
            get => _isInitialLoading;
            private set => SetProperty(ref _isInitialLoading, value);
        }

        private int _initialLoadingProgress;
        public int InitialLoadingProgress
        {
            get => _initialLoadingProgress;
            set => SetProperty(ref _initialLoadingProgress, value);
        }

        public bool IsAppLoading => ModsVM?.IsLoading ?? false;

        // Helper property to centralize the "Is anything busy?" logic
        private bool IsIdle => !(ModsVM?.IsLoading ?? false) &&
                               !(DownloaderVM?.IsOperationInProgress ?? false) &&
                               !(VramAnalysisVM?.IsBusy ?? false) &&
                               !_dialogService.IsAnyDialogOpen;


        // --- Constructor ---
        public MainViewModel(
            IPathService pathService,
            IConfigService configService,
            IDialogService dialogService,
            IApplicationNavigationService navigationService,
            IUpdaterService updaterService,
            IModsViewModel modsViewModel,
            IDownloaderViewModel downloaderViewModel,
            IGitModsViewModel gitModsViewModel,
            IVramAnalysisViewModel vramAnalysisViewModel)
        {
            _pathService = pathService;
            _configService = configService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _updaterService = updaterService;

            ModsVM = modsViewModel;
            DownloaderVM = downloaderViewModel;
            GitModsVM = gitModsViewModel;
            VramAnalysisVM = vramAnalysisViewModel;

            if (DownloaderVM != null)
            {
                DownloaderVM.DownloadCompletedAndRefreshNeeded += DownloaderVM_DownloadCompletedAndRefreshNeeded;
            }

            // Initialize Path Settings with empty values initially
            PathSettings = new PathSettings
            {
                GamePath = string.Empty,
                ConfigPath = string.Empty,
                ModsPath = string.Empty,
                GameVersion = "Loading..."
            };

            PathSettings.PropertyChanged += PathSettings_PropertyChanged;

            // Load actual path settings in background to avoid blocking constructor
            _pathInitializationTask = Task.Run(() =>
            {
                var gamePath = _pathService.GetGamePath();
                var configPath = _pathService.GetConfigPath();
                var modsPath = _pathService.GetModsPath();
                var gameVersion = _pathService.GetGameVersion();

                RunOnUIThread(() =>
                {
                    // Disable property changed handling while initializing to avoid multiple refreshes
                    PathSettings.PropertyChanged -= PathSettings_PropertyChanged;

                    PathSettings.GamePath = gamePath;
                    PathSettings.ConfigPath = configPath;
                    PathSettings.ModsPath = modsPath;
                    PathSettings.GameVersion = gameVersion;

                    PathSettings.PropertyChanged += PathSettings_PropertyChanged;

                    // Manually notify that the whole PathSettings object is ready
                    OnPropertyChanged(nameof(PathSettings));

                    // Trigger drive check after initialization
                    _ = Task.Run(() => CheckAndWarnDifferentDrives(PathSettings.GamePath));

                    // Update initial CanExecute states
                    ((DelegateCommand<string>)OpenFolderCommand).RaiseCanExecuteChanged();
                    ((DelegateCommand<string>)BrowsePathCommand).RaiseCanExecuteChanged();
                });
            });

            if (ModsVM != null)
            {
                ModsVM.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(IModsViewModel.IsLoading))
                    {
                        OnPropertyChanged(nameof(IsAppLoading));
                    }
                };
            }

            CurrentViewModel = ModsVM;
            _selectedTab = "Mods";
            if (ModsVM != null) ModsVM.IsViewActive = true;

            // --- Command Initialization using corrected CanExecute logic ---
            SwitchTabCommand = CreateCommand<string>(SwitchTab, CanExecuteGlobalActions);
            BrowsePathCommand = CreateCommand<string>(BrowsePath, CanBrowsePath);
            SettingsCommand = CreateCommand(OpenSettings, CanExecuteGlobalActions);
            RefreshCommand = CreateCommand(RefreshData, CanExecuteGlobalActions);
            OpenFolderCommand = CreateCommand<string>(OpenFolder, CanOpenFolder);
            AboutCommand = CreateCommand(() => _dialogService.ShowAboutDialog(), CanExecuteGlobalActions);

            // --- CORRECTED Command Observation Setup ---
            // Cast each command to its concrete type to access the 'ObservesProperty' method.
            var switchTabCmd = (DelegateCommand<string>)SwitchTabCommand;
            var browsePathCmd = (DelegateCommand<string>)BrowsePathCommand;
            var settingsCmd = (DelegateCommand)SettingsCommand;
            var refreshCmd = (DelegateCommand)RefreshCommand;
            var openFolderCmd = (DelegateCommand<string>)OpenFolderCommand;
            var aboutCmd = (DelegateCommand)AboutCommand;

            // Observe the IsLoading property on the ModsViewModel for all commands
            switchTabCmd.ObservesProperty(ModsVM, nameof(IModsViewModel.IsLoading));
            browsePathCmd.ObservesProperty(ModsVM, nameof(IModsViewModel.IsLoading));
            settingsCmd.ObservesProperty(ModsVM, nameof(IModsViewModel.IsLoading));
            refreshCmd.ObservesProperty(ModsVM, nameof(IModsViewModel.IsLoading));
            openFolderCmd.ObservesProperty(ModsVM, nameof(IModsViewModel.IsLoading));
            aboutCmd.ObservesProperty(ModsVM, nameof(IModsViewModel.IsLoading));

            // Also observe the IsOperationInProgress property on the DownloaderViewModel for all commands
            if (DownloaderVM != null)
            {
                switchTabCmd.ObservesProperty(DownloaderVM, nameof(IDownloaderViewModel.IsOperationInProgress));
                browsePathCmd.ObservesProperty(DownloaderVM, nameof(IDownloaderViewModel.IsOperationInProgress));
                settingsCmd.ObservesProperty(DownloaderVM, nameof(IDownloaderViewModel.IsOperationInProgress));
                refreshCmd.ObservesProperty(DownloaderVM, nameof(IDownloaderViewModel.IsOperationInProgress));
                openFolderCmd.ObservesProperty(DownloaderVM, nameof(IDownloaderViewModel.IsOperationInProgress));
                aboutCmd.ObservesProperty(DownloaderVM, nameof(IDownloaderViewModel.IsOperationInProgress));
            }

            if (VramAnalysisVM != null)
            {
                switchTabCmd.ObservesProperty(VramAnalysisVM, nameof(IVramAnalysisViewModel.IsBusy));
                browsePathCmd.ObservesProperty(VramAnalysisVM, nameof(IVramAnalysisViewModel.IsBusy));
                settingsCmd.ObservesProperty(VramAnalysisVM, nameof(IVramAnalysisViewModel.IsBusy));
                refreshCmd.ObservesProperty(VramAnalysisVM, nameof(IVramAnalysisViewModel.IsBusy));
                openFolderCmd.ObservesProperty(VramAnalysisVM, nameof(IVramAnalysisViewModel.IsBusy));
                aboutCmd.ObservesProperty(VramAnalysisVM, nameof(IVramAnalysisViewModel.IsBusy));
            }

            // Also observe the IsAnyDialogOpen property on the DialogService for all global commands
            if (_dialogService is INotifyPropertyChanged notifyDialogService)
            {
                switchTabCmd.ObservesProperty(notifyDialogService, nameof(IDialogService.IsAnyDialogOpen));
                browsePathCmd.ObservesProperty(notifyDialogService, nameof(IDialogService.IsAnyDialogOpen));
                settingsCmd.ObservesProperty(notifyDialogService, nameof(IDialogService.IsAnyDialogOpen));
                refreshCmd.ObservesProperty(notifyDialogService, nameof(IDialogService.IsAnyDialogOpen));
                openFolderCmd.ObservesProperty(notifyDialogService, nameof(IDialogService.IsAnyDialogOpen));
                aboutCmd.ObservesProperty(notifyDialogService, nameof(IDialogService.IsAnyDialogOpen));
            }


            // Initial CanExecute update for commands dependent on PathSettings properties
            ThreadHelper.EnsureUiThread(() =>
                {
                    ((DelegateCommand<string>)OpenFolderCommand).RaiseCanExecuteChanged();
                    ((DelegateCommand<string>)BrowsePathCommand).RaiseCanExecuteChanged();
                });

            // Subscribe to navigation requests
            _navigationService.TabSwitchRequested += OnTabSwitchRequested;
        }
        // --- END Constructor ---

        public async Task OnMainWindowLoadedAsync()
        {
            Debug.WriteLine("[MainViewModel] Main window has loaded. Starting initial tasks.", "OnMainWindowLoadedAsync");

            // Wait for path initialization to complete first
            if (_pathInitializationTask != null)
            {
                await _pathInitializationTask;
                Debug.WriteLine("[MainViewModel] Path initialization complete.", "OnMainWindowLoadedAsync");
            }

            // Check if paths are configured - if not, prompt user on first run
            if (string.IsNullOrEmpty(PathSettings.GamePath) || string.IsNullOrEmpty(PathSettings.ConfigPath))
            {
                Debug.WriteLine("[MainViewModel] Paths not configured. Showing first-run dialog.", "OnMainWindowLoadedAsync");
                _dialogService.ShowInformation("Welcome to RimSharp",
                    "It looks like this is your first time using RimSharp, or your configuration paths are missing.\n\n" +
                    "Please configure the game path and config path in the Settings dialog to get started.");
            }

            // Create a progress reporter for the splash screen
            var initialProgress = new Progress<(int current, int total, string message)>(update =>
            {
                InitialLoadingProgress = (int)((double)update.current / update.total * 100);
            });

            // Create a list to hold all startup tasks
            var startupTasks = new List<Task>();

            // Add the mod list initialization to the tasks - only if paths are configured
            if (ModsVM != null && !string.IsNullOrEmpty(PathSettings.GamePath))
            {
                startupTasks.Add(ModsVM.InitializeAsync(initialProgress));
            }
            else
            {
                IsInitialLoading = false;
            }

            // Add the update check to the tasks - only if paths are configured
            if (!string.IsNullOrEmpty(PathSettings.GamePath))
            {
                startupTasks.Add(CheckForUpdateAsync());
            }

            // If other ViewModels also need delayed initialization, add them here.
            // For example:
            // if (DownloaderVM != null) { startupTasks.Add(DownloaderVM.InitializeAsync()); }

            // Await all tasks to complete. This allows them to run concurrently.
            if (startupTasks.Count > 0)
            {
                await Task.WhenAll(startupTasks);
                IsInitialLoading = false;
            }

            Debug.WriteLine("[MainViewModel] All initial tasks complete.", "OnMainWindowLoadedAsync");
        }

        private async Task CheckForUpdateAsync()
        {
            Debug.WriteLine("[MainViewModel] Checking for game updates...");
            var (isUpdateAvailable, latestVersion) = await _updaterService.CheckForUpdateAsync();
            if (isUpdateAvailable && !string.IsNullOrEmpty(latestVersion))
            {
                IsUpdateAvailable = true;
                UpdateTooltipText = $"Version {latestVersion} is now available.";
                Debug.WriteLine($"[MainViewModel] Update found: {latestVersion}");
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateTooltipText = string.Empty;
                Debug.WriteLine("[MainViewModel] No new update found or check failed.");
            }
        }

        #region CanExecute Predicates

        /// <summary>
        /// A generic predicate for actions that should be disabled when any background process is running.
        /// </summary>
        private bool CanExecuteGlobalActions()
        {
            return IsIdle;
        }

        /// <summary>
        /// Overload for generic commands that should be disabled when any background process is running.
        /// </summary>
        private bool CanExecuteGlobalActions<T>(T parameter)
        {
            return IsIdle;
        }

        private bool CanOpenFolder(string pathType)
        {
            // First, check if the app is busy
            if (!IsIdle) return false;

            if (string.IsNullOrEmpty(pathType)) return false;

            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                _ => null
            };

            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }

        private bool CanBrowsePath(string pathType)
        {
            // First, check if the app is busy
            if (!IsIdle) return false;

            return !string.IsNullOrEmpty(pathType) &&
                   (pathType == "GamePath" || pathType == "ConfigPath");
        }

        #endregion

        private void OnTabSwitchRequested(object? sender, string tabName)
        { RunOnUIThread(() => SwitchTab(tabName)); }

        private void PathSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PathSettings)); // Notify UI about the whole object change

            string? key = null;
            string? value = null;
            bool refreshNeeded = false;
            bool pathSettingsChanged = false;
            string? potentiallyChangedGamePath = null; // Store the new game path temporarily

            switch (e.PropertyName)
            {
                case nameof(PathSettings.GamePath):
                    key = "game_folder";
                    value = PathSettings.GamePath;
                    potentiallyChangedGamePath = value;

                    // Update related properties without re-triggering path refresh
                    PathSettings.GameVersion = _pathService.GetGameVersion(value ?? string.Empty);
                    PathSettings.ModsPath = string.IsNullOrEmpty(value) ? string.Empty : Path.Combine(value, "Mods");
                    refreshNeeded = !string.IsNullOrEmpty(value); 
                    pathSettingsChanged = true;
                    break;
                case nameof(PathSettings.ConfigPath):
                    key = "config_folder";
                    value = PathSettings.ConfigPath;
                    refreshNeeded = true; // Config path change *might* require refresh (e.g., active mod list)
                    pathSettingsChanged = true;
                    break;
                case nameof(PathSettings.GameVersion):
                    // GameVersion change doesn't require saving config or full refresh,
                    // but notify UI if needed. PathSettings object notification handles this.
                    // Let's also re-check for updates if the version changes.
                    Task.Run(CheckForUpdateAsync);
                    break;
                case nameof(PathSettings.ModsPath):
                    // This property changed because GamePath changed. No further action needed here,
                    // but we need to signal that path settings did change for command updates.
                    pathSettingsChanged = true;
                    break;
            }

            if (key != null && value != null)
            {
                _configService.SetConfigValue(key, value);
                _configService.SaveConfig();
            }

            // --- Add Drive Check Here ---
            if (e.PropertyName == nameof(PathSettings.GamePath))
            {
                CheckAndWarnDifferentDrives(potentiallyChangedGamePath);
            }
            // --- End Drive Check ---


            if (refreshNeeded && RefreshCommand.CanExecute(null))
            {
                // It's better to let the user trigger refresh explicitly after path changes if it's slow,
                // or provide visual feedback that a refresh is recommended/needed.
                // For now, let's trigger it automatically.
                StatusMessage = "Path changed. Refreshing data...";
                RefreshCommand.Execute(null);
            }
            else if (refreshNeeded)
            {
                StatusMessage = "Path changed. Refresh recommended (click Refresh button).";
            }


            if (pathSettingsChanged)
            {
                // Update CanExecute for commands that depend on path validity
                RunOnUIThread(() => ((DelegateCommand<string>)OpenFolderCommand).RaiseCanExecuteChanged());
            }
        }

        /// <summary>
        /// Checks if the provided game path is on a different drive than the application
        /// and shows a warning dialog if it is.
        /// </summary>
        /// <param name="gamePath">The game path to check.</param>
        private void CheckAndWarnDifferentDrives(string? gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
            {
                return; // No path set, nothing to compare
            }

            try
            {
                string? appBasePath = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(appBasePath))
                {
                    Debug.WriteLine("[MainViewModel] Could not determine application base directory.");
                    return; // Cannot compare if we don't know where the app is
                }

                string? appRoot = Path.GetPathRoot(appBasePath);
                string? gameRoot = Path.GetPathRoot(gamePath);

                // Ensure roots could be determined and compare them (case-insensitive)
                if (!string.IsNullOrEmpty(appRoot) &&
                    !string.IsNullOrEmpty(gameRoot) &&
                    !string.Equals(appRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
                {
                    // Show warning dialog on the UI thread
                    RunOnUIThread(() =>
                    {
                        _dialogService.ShowWarning(
                            "Different Drives Detected",
                            $"The selected game folder ('{gameRoot}') is on a different drive than RimSharp ('{appRoot}').\n\n" +
                            "While RimSharp should still function, this configuration might occasionally lead to unexpected behavior or slightly reduced performance with file operations.\n\n" +
                            "For the most stable experience, it's recommended to install RimSharp on the same drive as your RimWorld installation."
                        );
                    });
                }
            }
            catch (ArgumentException ex)
            {
                // Path.GetPathRoot can throw ArgumentException for invalid paths
                Debug.WriteLine($"[MainViewModel] Error getting path root during drive check: {ex.Message}");
                // Optionally show a less specific error or just log it
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                Debug.WriteLine($"[MainViewModel] Unexpected error during drive check: {ex}");
                // Avoid crashing the app, just log the error.
            }
        }


        private void DownloaderVM_DownloadCompletedAndRefreshNeeded(object? sender, EventArgs e) // This is called after a download operation succeeds
        {
            StatusMessage = "Download complete. Refreshing mod list...";
            Debug.WriteLine("[MainViewModel] Received DownloadCompletedAndRefreshNeeded event. Mimicking RefreshCommand logic directly.");

            // This logic is a streamlined version of the main RefreshData method, tailored for this event.
            // We bypass the global CanExecute check on RefreshCommand because we know the download operation just finished,
            // which avoids a potential race condition with the IsOperationInProgress flag.

            try
            {
                Debug.WriteLine("[MainViewModel] Refreshing PathService cache after download...");
                // 1. Refresh paths service cache to recognize any new directories
                _pathService.RefreshPaths();

                // 2. Update MainViewModel's PathSettings properties from the refreshed service state
                string tempGame = _pathService.GetGamePath();
                string tempConfig = _pathService.GetConfigPath();
                string tempMods = _pathService.GetModsPath();
                string tempVersion = _pathService.GetGameVersion();

                // Check and set properties, which will trigger PropertyChanged events if values actually differ
                if (PathSettings.GamePath != tempGame) { PathSettings.GamePath = tempGame; }
                if (PathSettings.ConfigPath != tempConfig) { PathSettings.ConfigPath = tempConfig; }
                if (PathSettings.ModsPath != tempMods) { PathSettings.ModsPath = tempMods; }
                if (PathSettings.GameVersion != tempVersion) { PathSettings.GameVersion = tempVersion; }

                // 3. Trigger data refresh in the ModsViewModel via its own command
                Debug.WriteLine("[MainViewModel] Requesting ModsViewModel refresh...");
                if (ModsVM != null && ModsVM.RequestRefreshCommand.CanExecute(null))
                {
                    // We don't need to await this. The command will set IsLoading properties
                    // which will disable relevant UI elements.
                    ModsVM.RequestRefreshCommand.Execute(null);
                    StatusMessage = "Mod list refresh initiated.";
                }
                else
                {
                    StatusMessage = "Mod list refresh skipped: Another mod operation is already in progress.";
                    Debug.WriteLine("[MainViewModel] ModsVM.RequestRefreshCommand cannot execute; likely already loading.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during post-download refresh: {ex.Message}";
                Debug.WriteLine($"[MainViewModel] DownloaderVM_DownloadCompletedAndRefreshNeeded Error: {ex}");
                _dialogService.ShowError("Post-Download Refresh Error", $"An error occurred during the automatic refresh after download: {ex.Message}");
            }
        }



        private void OpenFolder(string pathType)
        {
            if (string.IsNullOrEmpty(pathType)) return;

            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                // "ModsPath" case removed
                _ => null
            };

            // Also allow opening the derived Mods path via the GamePath button's logic?
            // Let's keep it simple: only open explicitly defined paths for now.
            // User can navigate to Mods from GamePath easily.

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
                _dialogService.ShowWarning("Path Not Found", $"The path for '{pathType}' ('{path ?? "N/A"}') does not exist or is not set.");
            }
        }

        public INotifyPropertyChanged? CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public string SelectedTab
        {
            get => _selectedTab;
            private set => SetProperty(ref _selectedTab, value);
        }

        private void SwitchTab(string tabName)
        {
            if (string.IsNullOrEmpty(tabName)) return;

            Debug.WriteLine($"[MainViewModel] Attempting to switch tab to: {tabName}");
            INotifyPropertyChanged? nextViewModel = tabName switch
            {
                "Mods" => ModsVM,
                "Downloader" => DownloaderVM,
                "GitMods" => GitModsVM,
                "VRAM" => VramAnalysisVM,
                _ => null
            };

            if (nextViewModel != null && CurrentViewModel != nextViewModel)
            {
                CurrentViewModel = nextViewModel;
                SelectedTab = tabName;
                if (ModsVM != null) ModsVM.IsViewActive = tabName == "Mods";
                Debug.WriteLine($"[MainViewModel] Switched tab to: {tabName}");
            }
            else if (nextViewModel == null)
            {
                Debug.WriteLine($"[MainViewModel] Tab switch failed: No ViewModel found for tab '{tabName}'.");
            }
            else
            {
                Debug.WriteLine($"[MainViewModel] Tab switch ignored: Already on tab '{tabName}'.");
            }
        }


        private void BrowsePath(string pathType)
        {
            // CanExecute already checked
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = $"Select RimWorld {pathType.Replace("Path", "")} Folder";
                string? currentPath = pathType switch
                {
                    "GamePath" => PathSettings.GamePath,
                    "ConfigPath" => PathSettings.ConfigPath,
                    // "ModsPath" case removed
                    _ => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                // Ensure SelectedPath is valid if possible
                dialog.SelectedPath = (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                    ? currentPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    switch (pathType)
                    {
                        case "GamePath":
                            // Setting GamePath will trigger PropertyChanged, which handles updating ModsPath, Version, saving, drive check etc.
                            PathSettings.GamePath = selectedPath;
                            break;
                        case "ConfigPath":
                            PathSettings.ConfigPath = selectedPath;
                            break;
                            // "ModsPath" case removed
                    }
                    // CanExecute for OpenFolder/BrowsePath is handled by PathSettings_PropertyChanged
                }
            }
        }

        private void OpenSettings()
        {
            _dialogService.ShowInformation("Settings", "Settings dialog functionality is not yet implemented.");
        }

        // Centralized refresh logic
        private void RefreshData()
        {
            if (!CanExecuteGlobalActions()) return;

            StatusMessage = "Refreshing paths and mod data...";
            Debug.WriteLine("[MainViewModel] RefreshData executing...");

            bool pathsChanged = false;
            try
            {
                // 1. Refresh paths service cache
                _pathService.RefreshPaths();

                // 2. Update MainViewModel's PathSettings properties from the refreshed service state
                // Use temporary variables to minimize PropertyChanged events if values are the same
                string tempGame = _pathService.GetGamePath();
                string tempConfig = _pathService.GetConfigPath();
                string tempMods = _pathService.GetModsPath(); // Get the derived mods path
                string tempVersion = _pathService.GetGameVersion(); // Uses refreshed game path internally

                // Check and set properties, triggering PropertyChanged only if values actually differ
                if (PathSettings.GamePath != tempGame) { PathSettings.GamePath = tempGame; pathsChanged = true; }
                if (PathSettings.ConfigPath != tempConfig) { PathSettings.ConfigPath = tempConfig; pathsChanged = true; }
                if (PathSettings.ModsPath != tempMods) { PathSettings.ModsPath = tempMods; pathsChanged = true; } // Update derived ModsPath too
                if (PathSettings.GameVersion != tempVersion) { PathSettings.GameVersion = tempVersion; pathsChanged = true; } // Update version

                // 3. Trigger data refresh in the ModsViewModel via its command
                Debug.WriteLine("[MainViewModel] Requesting ModsViewModel refresh...");
                if (ModsVM.RequestRefreshCommand.CanExecute(null))
                {
                    ModsVM.RequestRefreshCommand.Execute(null);
                }
                else
                {
                    StatusMessage = "Mod refresh request skipped: Mod list operation already in progress.";
                    Debug.WriteLine("[MainViewModel] ModsVM.RequestRefreshCommand cannot execute.");
                }

                // 4. Update CanExecute state for commands dependent on paths (already done by PathSettings_PropertyChanged if pathsChanged=true)
                if (pathsChanged)
                {
                    // Explicitly trigger CanExecute updates in case PropertyChanged didn't fire for all dependencies
                    RunOnUIThread(() =>
                    {
                        ((DelegateCommand<string>)OpenFolderCommand).RaiseCanExecuteChanged();
                        ((DelegateCommand<string>)BrowsePathCommand).RaiseCanExecuteChanged();
                    });
                }

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
                // Provide slightly more accurate completion feedback
                Task.Delay(3500).ContinueWith(t =>
                {
                    if (StatusMessage == "Refresh initiated...")
                    {
                        StatusMessage = ModsVM.IsLoading ? "Mod list refresh in progress..." : "Refresh complete.";
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());

                Debug.WriteLine("[MainViewModel] RefreshData finished trigger.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return; // Use specific field for this class

            if (disposing)
            {
                // Unsubscribe from events
                Debug.WriteLine("[MainViewModel] Disposing: Unsubscribing from events.");
                if (_navigationService != null)
                {
                    _navigationService.TabSwitchRequested -= OnTabSwitchRequested;
                }
                if (PathSettings != null)
                {
                    PathSettings.PropertyChanged -= PathSettings_PropertyChanged;
                }
                if (DownloaderVM != null)
                {
                    DownloaderVM.DownloadCompletedAndRefreshNeeded -= DownloaderVM_DownloadCompletedAndRefreshNeeded;
                }

                 // Dispose child ViewModels if they are IDisposable (important if they are singletons resolved here)
                 (ModsVM as IDisposable)?.Dispose();
                (DownloaderVM as IDisposable)?.Dispose();
                (GitModsVM as IDisposable)?.Dispose();
                (VramAnalysisVM as IDisposable)?.Dispose();
                // Dispose other managed resources if any
            }

            // Call base dispose AFTER derived class cleanup
            base.Dispose(disposing);
            _disposed = true; // Set flag after base call completes its logic
            Debug.WriteLine("[MainViewModel] Disposed.");
        }
        ~MainViewModel()
        {
            Dispose(false);
        }
    }
}
