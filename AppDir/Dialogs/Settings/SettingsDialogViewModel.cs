#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Shared.Models;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Infrastructure.Logging;

namespace RimSharp.AppDir.Dialogs
{
    public class AiProviderSettingsRow : ViewModelBase
    {
        private readonly IAIProviderClient _client;

        public string ProviderId { get; }
        public string DisplayName { get; }

        private string _apiKey;
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (SetProperty(ref _apiKey, value))
                    OnPropertyChanged(nameof(IsConfigured));
            }
        }

        private string _model;
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        public AiProviderSettingsRow(IAIProviderClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            ProviderId = client.ProviderId;
            DisplayName = client.DisplayName;
            _apiKey = client.GetApiKey() ?? string.Empty;
            _model = client.GetModel() ?? string.Empty;
        }

        public void WriteBack()
        {
            _client.SetApiKey(ApiKey?.Trim() ?? string.Empty);
            _client.SetModel(Model?.Trim() ?? string.Empty);
        }
    }

    public class SettingsDialogViewModel : DialogViewModelBase<bool>
    {
        private const string GameFolderKey = "game_folder";
        private const string ConfigFolderKey = "config_folder";
        private const string UpdateChannelKey = "update_channel";
        private const string GameDistributionKey = "game_distribution";
        private const string LegacyLauncherKey = "game_launcher";
        private const string SteamInstallationKey = "steam_installation";
        private const string SteamWorkshopFolderKey = "steam_workshop_folder";
        private const string SteamIntegrationKey = "steam_integration";

        private readonly IConfigService _configService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly GameInstallationDetectionService _gameInstallationDetectionService;
        private readonly ILoggerService _logger;

        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }

        private string _configPath = string.Empty;
        public string ConfigPath { get => _configPath; set => SetProperty(ref _configPath, value); }

        private string _updateChannel = "stable";
        public string UpdateChannel { get => _updateChannel; set => SetProperty(ref _updateChannel, value); }

        private GameDistribution _selectedDistribution = GameDistribution.NonSteam;
        public GameDistribution SelectedDistribution 
        { 
            get => _selectedDistribution; 
            set 
            { 
                if (SetProperty(ref _selectedDistribution, value))
                {
                    ApplyDistributionPaths(userInitiatedChange: true);
                }
            } 
        }

        private GameInstallationCandidate? _steamCandidate;
        public GameInstallationCandidate? SteamCandidate
        {
            get => _steamCandidate;
            private set
            {
                if (SetProperty(ref _steamCandidate, value))
                    OnPropertyChanged(nameof(SteamAvailable));
            }
        }

        private GameInstallationCandidate? _nonSteamCandidate;
        public GameInstallationCandidate? NonSteamCandidate
        {
            get => _nonSteamCandidate;
            private set
            {
                if (SetProperty(ref _nonSteamCandidate, value))
                    OnPropertyChanged(nameof(NonSteamAvailable));
            }
        }

        public bool SteamAvailable => SteamCandidate != null && SteamCandidate.IsValid;
        public bool NonSteamAvailable => NonSteamCandidate != null && NonSteamCandidate.IsValid;

        public ICommand BrowseGamePathCommand { get; }
        public ICommand BrowseConfigPathCommand { get; }
        public ICommand OpenGameFolderCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public IReadOnlyList<AiProviderSettingsRow> Providers { get; }
        public bool ShowAiProviders { get; }

        public SettingsDialogViewModel(
            IConfigService configService,
            IFileDialogService? fileDialogService,
            ILoggerService logger,
            IEnumerable<IAIProviderClient>? providers,
            bool showAiProviders = true)
        : base("RimSharp Settings")
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _fileDialogService = fileDialogService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameInstallationDetectionService = new GameInstallationDetectionService();
            ShowAiProviders = showAiProviders;

            _gamePath = _configService.GetConfigValue(GameFolderKey) ?? string.Empty;
            _configPath = _configService.GetConfigValue(ConfigFolderKey) ?? string.Empty;

            var channel = _configService.GetConfigValue(UpdateChannelKey);
            _updateChannel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel;

            LoadSettings();

            Providers = (providers ?? Array.Empty<IAIProviderClient>())
                .Select(p => new AiProviderSettingsRow(p))
                .ToList();

            BrowseGamePathCommand = CreateAsyncCommand(BrowseGamePathAsync);
            BrowseConfigPathCommand = CreateAsyncCommand(BrowseConfigPathAsync);
            OpenGameFolderCommand = CreateCommand(() => OpenFolder(GamePath));
            OpenConfigFolderCommand = CreateCommand(() => OpenFolder(ConfigPath));
            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(() => CloseDialog(false));
        }

        private void LoadSettings()
        {
            // Detect installations
            SteamCandidate = _gameInstallationDetectionService.DetectSteamInstallation();
            NonSteamCandidate = _gameInstallationDetectionService.DetectNonSteamInstallation();

            // Check if user already has a saved preference in config
            var savedDistStr = _configService.GetConfigValue(GameDistributionKey) 
                            ?? _configService.GetConfigValue(LegacyLauncherKey);

            if (!string.IsNullOrEmpty(savedDistStr))
            {
                // After first run, strictly respect what is saved in cfg
                if (string.Equals(savedDistStr, "Steam", StringComparison.OrdinalIgnoreCase) && SteamAvailable)
                {
                    _selectedDistribution = GameDistribution.Steam;
                }
                else
                {
                    _selectedDistribution = GameDistribution.NonSteam;
                }
            }
            else
            {
                // First load (not in cfg yet):
                // If Steam installation is detected -> auto-pick Steam, else Non-Steam
                _selectedDistribution = SteamAvailable ? GameDistribution.Steam : GameDistribution.NonSteam;
            }

            OnPropertyChanged(nameof(SelectedDistribution));

            // Only fill empty path fields on initial load
            ApplyDistributionPaths(userInitiatedChange: false);
        }

        private void ApplyDistributionPaths(bool userInitiatedChange)
        {
            if (SelectedDistribution == GameDistribution.Steam && SteamCandidate != null)
            {
                if (userInitiatedChange || string.IsNullOrEmpty(GamePath))
                {
                    GamePath = SteamCandidate.GameFolder;
                }
                if (userInitiatedChange || string.IsNullOrEmpty(ConfigPath))
                {
                    ConfigPath = SteamCandidate.ConfigFolder;
                }
            }
            else if (SelectedDistribution == GameDistribution.NonSteam && NonSteamCandidate != null)
            {
                if (userInitiatedChange || string.IsNullOrEmpty(GamePath))
                {
                    GamePath = NonSteamCandidate.GameFolder;
                }
                if (userInitiatedChange || string.IsNullOrEmpty(ConfigPath))
                {
                    ConfigPath = NonSteamCandidate.ConfigFolder;
                }
            }
        }

        private async Task BrowseGamePathAsync() => await BrowseFolderAsync(
            "Select RimWorld Game Folder", _gamePath, p => GamePath = p);

        private async Task BrowseConfigPathAsync() => await BrowseFolderAsync(
            "Select RimWorld Config Folder", _configPath, p => ConfigPath = p);

        private async Task BrowseFolderAsync(string title, string? initialDirectory, Action<string> setValue)
        {
            if (_fileDialogService == null) return;

            var (success, path) = await _fileDialogService.ShowOpenFolderDialogAsync(title, initialDirectory);
            if (success && !string.IsNullOrWhiteSpace(path))
            {
                setValue(path);
            }
        }

        private static void OpenFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            RimSharp.Core.Services.SystemLauncher.OpenInFileManager(path);
        }

        private void Save()
        {
            _configService.SetConfigValue(GameFolderKey, GamePath?.Trim() ?? string.Empty);
            _configService.SetConfigValue(ConfigFolderKey, ConfigPath?.Trim() ?? string.Empty);
            _configService.SetConfigValue(UpdateChannelKey, UpdateChannel);
            _configService.SetConfigValue(GameDistributionKey, SelectedDistribution.ToString());

            if (SelectedDistribution == GameDistribution.Steam && SteamCandidate != null)
            {
                _configService.SetConfigValue(SteamInstallationKey, "true");
                _configService.SetConfigValue(SteamWorkshopFolderKey, SteamCandidate.WorkshopFolder ?? string.Empty);
                _configService.SetConfigValue(SteamIntegrationKey, SteamCandidate.HasSteamClientIntegration.ToString());
            }
            else
            {
                _configService.SetConfigValue(SteamInstallationKey, "false");
                _configService.SetConfigValue(SteamWorkshopFolderKey, string.Empty);
                _configService.SetConfigValue(SteamIntegrationKey, "false");
            }

            foreach (var row in Providers)
            {
                row.WriteBack();
            }

            _configService.SaveConfig();
            CloseDialog(true);
        }
    }
}