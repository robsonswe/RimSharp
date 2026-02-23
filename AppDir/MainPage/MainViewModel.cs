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
using RimSharp.Core.Extensions;
using RimSharp.Features.GitModManager.ViewModels;
using RimSharp.Core.Commands.Base;
using RimSharp.Features.VramAnalysis.ViewModels;
using ReactiveUI;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

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

        public PathSettings PathSettings { get; }

        private Task? _pathInitializationTask;

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

            PathSettings = new PathSettings
            {
                GamePath = string.Empty,
                ConfigPath = string.Empty,
                ModsPath = string.Empty,
                GameVersion = "Loading..."
            };

            PathSettings.PropertyChanged += PathSettings_PropertyChanged;

            _pathInitializationTask = Task.Run(() =>
            {
                var gamePath = _pathService.GetGamePath();
                var configPath = _pathService.GetConfigPath();
                var modsPath = _pathService.GetModsPath();
                var gameVersion = _pathService.GetGameVersion();

                RunOnUIThread(() =>
                {
                    PathSettings.PropertyChanged -= PathSettings_PropertyChanged;
                    PathSettings.GamePath = gamePath;
                    PathSettings.ConfigPath = configPath;
                    PathSettings.ModsPath = modsPath;
                    PathSettings.GameVersion = gameVersion;
                    PathSettings.PropertyChanged += PathSettings_PropertyChanged;

                    this.RaisePropertyChanged(nameof(PathSettings));
                    _ = Task.Run(() => CheckAndWarnDifferentDrives(PathSettings.GamePath));
                });
            });

            CurrentViewModel = ModsVM;
            _selectedTab = "Mods";
            ModsVM.IsViewActive = true;

            SwitchTabCommand = CreateCommand<string>(SwitchTab, CanExecuteGlobalActions);
            BrowsePathCommand = CreateAsyncCommand<string>(BrowsePathAsync, CanBrowsePath);
            SettingsCommand = CreateCommand(OpenSettings, CanExecuteGlobalActions);
            RefreshCommand = CreateCommand(RefreshData, CanExecuteGlobalActions);
            OpenFolderCommand = CreateCommand<string>(OpenFolder, CanOpenFolder);
            AboutCommand = CreateAsyncCommand(() => _dialogService.ShowAboutDialog(), CanExecuteGlobalActions);

            _navigationService.TabSwitchRequested += OnTabSwitchRequested;
        }

        public async Task OnMainWindowLoadedAsync()
        {
            if (_pathInitializationTask != null) await _pathInitializationTask;

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

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
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
            var (result, selectedPath) = await _dialogService.ShowOpenFolderDialogAsync(
                $"Select RimWorld {pathType.Replace("Path", "")} Folder");
            
            if (result && !string.IsNullOrEmpty(selectedPath))
            {
                if (pathType == "GamePath") PathSettings.GamePath = selectedPath;
                else if (pathType == "ConfigPath") PathSettings.ConfigPath = selectedPath;
            }
        }

        private void OpenSettings() => _dialogService.ShowInformation("Settings", "Not yet implemented.");

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
