#nullable enable
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Features.ModManager.ViewModels;
using RimSharp.Features.WorkshopDownloader.ViewModels;
using RimSharp.Shared.Models;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Services.ModManagement;
using RimSharp.Core.Extensions;
using RimSharp.Features.GitModManager.ViewModels;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.VramAnalysis.ViewModels;
using ReactiveUI;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using RimSharp.Services.Workshop;

namespace RimSharp.AppDir.MainPage
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IPathService _pathService;
        private readonly IConfigService _configService;
        private readonly IDialogService _dialogService;
        private readonly IApplicationNavigationService _navigationService;
        private readonly IUpdaterService _updaterService;

        private string _selectedTab = "Mods";
        private INotifyPropertyChanged? _currentViewModel;

        public IModsViewModel ModsVM { get; }
        public IDownloaderViewModel? DownloaderVM { get; }
        public IGitModsViewModel GitModsVM { get; }
        public IVramAnalysisViewModel VramAnalysisVM { get; }

        // Workshop Management Services
        public ISteamSubscriptionService? SteamSubscriptionService { get; }
        public IWorkshopUpdateService? WorkshopUpdateService { get; }

        public PathSettings PathSettings { get; }

        // Steam detection and mod classification
        private readonly SteamDetectionService _steamDetectionService;
        private readonly ModClassificationService _modClassificationService;
        private GameInstance _gameInstance = new GameInstance();

        private Task? _pathInitializationTask;
        private bool _isInitializingPaths;

        public ICommand SwitchTabCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand AboutCommand { get; }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
        }

        private string _updateTooltipText = string.Empty;
        public string UpdateTooltipText
        {
            get => _updateTooltipText;
            set => this.RaiseAndSetIfChanged(ref _updateTooltipText, value);
        }

        private bool _isInitialLoading = true;
        public bool IsInitialLoading
        {
            get => _isInitialLoading;
            private set => this.RaiseAndSetIfChanged(ref _isInitialLoading, value);
        }

        private int _initialLoadingProgress;
        public int InitialLoadingProgress
        {
            get => _initialLoadingProgress;
            set => this.RaiseAndSetIfChanged(ref _initialLoadingProgress, value);
        }

        public bool IsAppLoading => ModsVM.IsLoading;

        private bool IsIdle => !ModsVM.IsLoading &&
                               !(DownloaderVM?.IsOperationInProgress ?? false) &&
                               !(VramAnalysisVM?.IsBusy ?? false) &&
                               !_dialogService.IsAnyDialogOpen;

        public MainViewModel(
            IPathService pathService,
            IConfigService configService,
            IDialogService dialogService,
            IApplicationNavigationService navigationService,
            IUpdaterService updaterService,
            IModsViewModel modsViewModel,
            IDownloaderViewModel downloaderViewModel,
            IGitModsViewModel gitModsViewModel,
            IVramAnalysisViewModel vramAnalysisViewModel,
            ISteamSubscriptionService? steamSubscriptionService = null,
            IWorkshopUpdateService? workshopUpdateService = null,
            GameInstance? gameInstance = null)
        {
            _pathService = pathService;
            _configService = configService;
            _dialogService = dialogService;
            _navigationService = navigationService;
            _updaterService = updaterService;

            _gameInstance = gameInstance ?? new GameInstance();

            // Initialize Steam detection and mod classification
            _steamDetectionService = new SteamDetectionService();
            _modClassificationService = new ModClassificationService(_gameInstance);

            ModsVM = modsViewModel;
            DownloaderVM = downloaderViewModel;
            GitModsVM = gitModsViewModel;
            VramAnalysisVM = vramAnalysisViewModel;
            
            // Initialize workshop services (optional)
            SteamSubscriptionService = steamSubscriptionService;
            WorkshopUpdateService = workshopUpdateService;

            if (DownloaderVM != null)
            {
                DownloaderVM.DownloadCompletedAndRefreshNeeded += DownloaderVM_DownloadCompletedAndRefreshNeeded;
            }

            PathSettings = new PathSettings
            {
                GamePath = string.Empty,
                ConfigPath = string.Empty,
                ModsPath = string.Empty,
                GameVersion = "Loading..."
            };

            PathSettings.PropertyChanged += PathSettings_PropertyChanged;

            _pathInitializationTask = Task.Run(async () =>
            {
                var gamePath = _pathService.GetGamePath();
                var configPath = _pathService.GetConfigPath();
                var modsPath = _pathService.GetModsPath();
                var gameVersion = _pathService.GetGameVersion();

                RunOnUIThread(() =>
                {
                    // Suppresses persistence while the initial path values are
                    // applied, so initialization does not rewrite the config
                    // it just read. The flag is only held for this synchronous
                    // block; changes made outside it are always persisted.
                    _isInitializingPaths = true;
                    try
                    {
                        PathSettings.GamePath = gamePath;
                        PathSettings.ConfigPath = configPath;
                        PathSettings.ModsPath = modsPath;
                        PathSettings.GameVersion = gameVersion;
                    }
                    finally
                    {
                        _isInitializingPaths = false;
                    }

                    this.RaisePropertyChanged(nameof(PathSettings));
                    _ = Task.Run(() => CheckAndWarnDifferentDrives(PathSettings.GamePath));
                });

                // Detect Steam installation and configure game instance
                await DetectSteamInstallation();
            });

            CurrentViewModel = ModsVM;
            _selectedTab = "Mods";
            ModsVM.IsViewActive = true;

            SwitchTabCommand = CreateCommand<string>(SwitchTab, CanExecuteGlobalActions);
            BrowsePathCommand = CreateAsyncCommand<string>(BrowsePathAsync, CanBrowsePath);
            SettingsCommand = CreateAsyncCommand(OpenSettings, CanExecuteGlobalActions);
            RefreshCommand = CreateCommand(RefreshData, CanExecuteGlobalActions);
            OpenFolderCommand = CreateCommand<string>(OpenFolder, CanOpenFolder);
            AboutCommand = CreateAsyncCommand(() => _dialogService.ShowAboutDialog(), CanExecuteGlobalActions);

            _navigationService.TabSwitchRequested += OnTabSwitchRequested;
        }

        /// <summary>
        /// Completes when the initial game/config paths have been applied to
        /// <see cref="PathSettings"/>.
        /// </summary>
        public Task WaitForPathInitializationAsync() => _pathInitializationTask ?? Task.CompletedTask;

        /// <summary>
        /// Detect Steam installation and configure the game instance.
        /// </summary>
        private async Task DetectSteamInstallation()
        {
            try
            {
                if (!_gameInstance.IsSteamInstallation)
                {
                    _steamDetectionService.DetectAndConfigureSteam(_gameInstance);
                }

                if (_gameInstance.IsSteamInstallation)
                {
                    StatusMessage = $"Steam installation detected at {_gameInstance.GameFolder}";
                }
                else
                {
                    StatusMessage = "No Steam installation detected, using non-Steam configuration";
                    
                    // Set up non-Steam configuration based on existing paths
                    if (!string.IsNullOrEmpty(PathSettings.GamePath))
                    {
                        _gameInstance.GameFolder = PathSettings.GamePath;
                        _gameInstance.LocalFolder = PathSettings.ModsPath;
                        _gameInstance.ConfigFolder = PathSettings.ConfigPath;
                        _gameInstance.IsSteamInstallation = false;
                    }
                }
                
                // Validate and fix any inconsistencies
                _gameInstance.ValidateAndFix();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error detecting Steam installation: {ex.Message}";
                _gameInstance.IsSteamInstallation = false;
            }
        }

        public async Task OnMainWindowLoadedAsync()
        {
            await WaitForPathInitializationAsync();

            if (string.IsNullOrEmpty(PathSettings.GamePath) || string.IsNullOrEmpty(PathSettings.ConfigPath))
            {
                await _dialogService.ShowInformation("Welcome to RimSharp",
                    "Please configure the game path and config path in the Settings dialog to get started.");
            }

            var initialProgress = new Progress<(int current, int total, string message)>(update =>
            {
                if (update.total > 0)
                {
                    InitialLoadingProgress = (int)((double)update.current / update.total * 100);
                }
                else
                {
                    InitialLoadingProgress = 0;
                }
            });

            var startupTasks = new List<Task>();
            if (!string.IsNullOrEmpty(PathSettings.GamePath))
            {
                startupTasks.Add(ModsVM.InitializeAsync(initialProgress));
                startupTasks.Add(CheckForUpdateAsync());
            }
            else
            {
                IsInitialLoading = false;
            }

            if (startupTasks.Count > 0)
            {
                await Task.WhenAll(startupTasks);
                IsInitialLoading = false;
            }
        }

        private async Task CheckForUpdateAsync()
        {
            var (isUpdateAvailable, latestVersion) = await _updaterService.CheckForUpdateAsync();
            if (isUpdateAvailable && !string.IsNullOrEmpty(latestVersion))
            {
                IsUpdateAvailable = true;
                UpdateTooltipText = $"Version {latestVersion} is now available.";
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateTooltipText = string.Empty;
            }
        }

        private bool CanExecuteGlobalActions() => IsIdle;
        private bool CanExecuteGlobalActions<T>(T parameter) => IsIdle;

        private bool CanOpenFolder(string pathType)
        {
            if (!IsIdle || string.IsNullOrEmpty(pathType)) return false;
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
            return IsIdle && !string.IsNullOrEmpty(pathType) &&
                   (pathType == "GamePath" || pathType == "ConfigPath");
        }

        private void OnTabSwitchRequested(object? sender, string tabName) => RunOnUIThread(() => SwitchTab(tabName));

        private void PathSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isInitializingPaths) return;

            this.RaisePropertyChanged(nameof(PathSettings));

            string? key = null;
            string? value = null;
            bool refreshNeeded = false;

            switch (e.PropertyName)
            {
                case nameof(PathSettings.GamePath):
                    key = "game_folder";
                    value = PathSettings.GamePath;
                    PathSettings.GameVersion = _pathService.GetGameVersion(value ?? string.Empty);
                    PathSettings.ModsPath = string.IsNullOrEmpty(value) ? string.Empty : Path.Combine(value, "Mods");
                    refreshNeeded = !string.IsNullOrEmpty(value);
                    CheckAndWarnDifferentDrives(value);
                    break;
                case nameof(PathSettings.ConfigPath):
                    key = "config_folder";
                    value = PathSettings.ConfigPath;
                    refreshNeeded = true;
                    break;
                case nameof(PathSettings.GameVersion):
                    Task.Run(CheckForUpdateAsync);
                    break;
            }

            if (key != null && value != null)
            {
                _configService.SetConfigValue(key, value);
                _configService.SaveConfig();
            }

            if (refreshNeeded)
            {
                if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
                else StatusMessage = "Path changed. Refresh recommended.";
            }
        }

        private void CheckAndWarnDifferentDrives(string? gamePath)
        {
            if (string.IsNullOrEmpty(gamePath)) return;
            try
            {
                string? appBasePath = AppContext.BaseDirectory;
                string? appRoot = Path.GetPathRoot(appBasePath);
                string? gameRoot = Path.GetPathRoot(gamePath);

                if (!string.IsNullOrEmpty(appRoot) && !string.IsNullOrEmpty(gameRoot) &&
                    !string.Equals(appRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
                {
                    RunOnUIThread(() => _dialogService.ShowWarning("Different Drives Detected", "RimSharp and the game are on different drives."));
                }
            }
            catch { }
        }

        private void DownloaderVM_DownloadCompletedAndRefreshNeeded(object? sender, EventArgs e)
        {
            StatusMessage = "Download complete. Refreshing mod list...";
            try
            {
                _pathService.RefreshPaths();
                PathSettings.GamePath = _pathService.GetGamePath();
                PathSettings.ConfigPath = _pathService.GetConfigPath();
                PathSettings.ModsPath = _pathService.GetModsPath();
                PathSettings.GameVersion = _pathService.GetGameVersion();

                if (ModsVM.RequestRefreshCommand.CanExecute(null)) ModsVM.RequestRefreshCommand.Execute(null);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Post-Download Refresh Error", ex.Message);
            }
        }

        private void OpenFolder(string pathType)
        {
            string? path = pathType switch
            {
                "GamePath" => PathSettings.GamePath,
                "ConfigPath" => PathSettings.ConfigPath,
                _ => null
            };

            RimSharp.Core.Services.SystemLauncher.OpenInFileManager(path ?? string.Empty);
        }

        public INotifyPropertyChanged? CurrentViewModel
        {
            get => _currentViewModel;
            set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
        }

        public string SelectedTab
        {
            get => _selectedTab;
            private set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
        }

        private void SwitchTab(string tabName)
        {
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
                ModsVM.IsViewActive = tabName == "Mods";
            }
        }

        private async Task BrowsePathAsync(string pathType)
        {
            string? initialDirectory = pathType switch
            {
                "GamePath" => RimWorldDefaultPaths.GetGameFolderHint(),
                "ConfigPath" => RimWorldDefaultPaths.GetConfigFolder(),
                _ => null
            };

            var (result, selectedPath) = await _dialogService.ShowOpenFolderDialogAsync(
                $"Select RimWorld {pathType.Replace("Path", "")} Folder", initialDirectory ?? string.Empty);

            if (result && !string.IsNullOrEmpty(selectedPath))
            {
                if (pathType == "GamePath") PathSettings.GamePath = selectedPath;
                else if (pathType == "ConfigPath") PathSettings.ConfigPath = selectedPath;
            }
        }

        private async Task OpenSettings()
        {
            var saved = await _dialogService.ShowSettingsDialogAsync();
            if (!saved) return;

            // Re-sync the path settings from the config the dialog just
            // wrote (game/config folders may both have changed).
            _isInitializingPaths = true;
            try
            {
                PathSettings.GamePath = _pathService.GetGamePath();
                PathSettings.ConfigPath = _pathService.GetConfigPath();
                PathSettings.ModsPath = _pathService.GetModsPath();
                PathSettings.GameVersion = _pathService.GetGameVersion();
            }
            finally
            {
                _isInitializingPaths = false;
            }
        }

        /// <summary>
        /// Startup gate: the app MUST NOT operate with unresolved Game/Config
        /// folders (that is what produced the confusing "app opens with empty
        /// lists" runs). Shows the Settings dialog repeatedly until both
        /// paths resolve. Returns false when the user gives up — the caller
        /// (App) then shuts the application down instead of loading data.
        /// </summary>
        public async Task<bool> EnsurePathsConfiguredAsync()
        {
            bool PathsValid() =>
                !string.IsNullOrEmpty(_pathService.GetGamePath()) &&
                !string.IsNullOrEmpty(_pathService.GetConfigPath());

            while (!PathsValid())
            {
                await _dialogService.ShowSettingsDialogAsync(showAiProviders: false);
                _pathService.RefreshPaths();
                if (PathsValid()) break;

                var quit = await _dialogService.ShowConfirmationAsync(
                    "RimSharp Needs Valid Paths",
                    "The Game and/or Config folder could not be resolved, so RimSharp cannot load mods.\n\n" +
                    "Open Settings again to configure them, or quit the application.",
                    showCancel: true);
                if (quit != RimSharp.AppDir.Dialogs.MessageDialogResult.OK &&
                    quit != RimSharp.AppDir.Dialogs.MessageDialogResult.Yes)
                {
                    return false; // user chose to quit
                }
            }

            // Paths just became valid (or already were) — sync the header
            // badge and derived path UI state.
            _isInitializingPaths = true;
            try
            {
                PathSettings.GamePath = _pathService.GetGamePath();
                PathSettings.ConfigPath = _pathService.GetConfigPath();
                PathSettings.ModsPath = _pathService.GetModsPath();
                PathSettings.GameVersion = _pathService.GetGameVersion();
                
                // Update game instance with path information
                if (!string.IsNullOrEmpty(PathSettings.GamePath))
                {
                    _gameInstance.GameFolder = PathSettings.GamePath;
                    _gameInstance.LocalFolder = PathSettings.ModsPath;
                    _gameInstance.ConfigFolder = PathSettings.ConfigPath;
                }
            }
            finally
            {
                _isInitializingPaths = false;
            }
            return true;
        }

        private void RefreshData()
        {
            if (!IsIdle) return;
            try
            {
                _pathService.RefreshPaths();
                PathSettings.GamePath = _pathService.GetGamePath();
                PathSettings.ConfigPath = _pathService.GetConfigPath();
                PathSettings.ModsPath = _pathService.GetModsPath();
                PathSettings.GameVersion = _pathService.GetGameVersion();

                if (ModsVM.RequestRefreshCommand.CanExecute(null)) ModsVM.RequestRefreshCommand.Execute(null);
                StatusMessage = "Refresh initiated...";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Refresh Error", ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _navigationService.TabSwitchRequested -= OnTabSwitchRequested;
                PathSettings.PropertyChanged -= PathSettings_PropertyChanged;
                if (DownloaderVM != null) DownloaderVM.DownloadCompletedAndRefreshNeeded -= DownloaderVM_DownloadCompletedAndRefreshNeeded;

                (ModsVM as IDisposable)?.Dispose();
                (DownloaderVM as IDisposable)?.Dispose();
                (GitModsVM as IDisposable)?.Dispose();
                (VramAnalysisVM as IDisposable)?.Dispose();
            }
            base.Dispose(disposing);
            _disposed = true;
        }
    }
}
