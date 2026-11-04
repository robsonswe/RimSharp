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
        private const string SteamInstallationKey = "steam_installation";
        private const string SteamWorkshopFolderKey = "steam_workshop_folder";
        private const string SteamIntegrationKey = "steam_integration";
        private const string SteamProtocolKey = "steam_protocol";
        private const string SteamCmdPathKey = "steamcmd_path";
        private const string SteamCmdCacheKey = "steamcmd_cache";

        private readonly IConfigService _configService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly SteamDetectionService _steamDetectionService;

        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }

        private string _configPath = string.Empty;
        public string ConfigPath { get => _configPath; set => SetProperty(ref _configPath, value); }

        private string _updateChannel = "stable";
        public string UpdateChannel { get => _updateChannel; set => SetProperty(ref _updateChannel, value); }

        // Steam settings
        private GameInstance _gameInstance = new GameInstance();
        public GameInstance GameInstance { get => _gameInstance; set => SetProperty(ref _gameInstance, value); }

        public IReadOnlyList<AiProviderSettingsRow> Providers { get; }
        public bool ShowAiProviders { get; }

        public ICommand BrowseGamePathCommand { get; }
        public ICommand BrowseConfigPathCommand { get; }
        public ICommand OpenGameFolderCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public SettingsDialogViewModel(
            IConfigService configService,
            IFileDialogService? fileDialogService,
            IEnumerable<IAIProviderClient>? providers,
            bool showAiProviders = true)
        : base("RimSharp Settings")
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _fileDialogService = fileDialogService;
            _steamDetectionService = new SteamDetectionService();
            ShowAiProviders = showAiProviders;

            _gamePath = _configService.GetConfigValue(GameFolderKey);
            _configPath = _configService.GetConfigValue(ConfigFolderKey);

            // Load Steam settings
            LoadSteamSettings();

            // Se for vazio ou nulo no arquivo de config, assume "stable"
            var channel = _configService.GetConfigValue(UpdateChannelKey);
            _updateChannel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel;

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

        /// <summary>
        /// Load Steam settings from configuration.
        /// </summary>
        private void LoadSteamSettings()
        {
            // Try to detect Steam installation
            var steamDetected = _steamDetectionService.DetectAndConfigureSteam(_gameInstance);
            
            // Override with saved settings if they exist
            var savedSteamInstallation = _configService.GetConfigValue(SteamInstallationKey);
            if (!string.IsNullOrEmpty(savedSteamInstallation))
            {
                _gameInstance.IsSteamInstallation = bool.Parse(savedSteamInstallation);
            }

            var savedWorkshopFolder = _configService.GetConfigValue(SteamWorkshopFolderKey);
            if (!string.IsNullOrEmpty(savedWorkshopFolder))
            {
                _gameInstance.WorkshopFolder = savedWorkshopFolder;
            }

            var savedIntegration = _configService.GetConfigValue(SteamIntegrationKey);
            if (!string.IsNullOrEmpty(savedIntegration))
            {
                _gameInstance.SteamClientIntegration = bool.Parse(savedIntegration);
            }

            var savedProtocol = _configService.GetConfigValue(SteamProtocolKey);
            if (!string.IsNullOrEmpty(savedProtocol))
            {
                _gameInstance.LaunchViaSteamProtocol = bool.Parse(savedProtocol);
            }

            var savedSteamCmdPath = _configService.GetConfigValue(SteamCmdPathKey);
            if (!string.IsNullOrEmpty(savedSteamCmdPath))
            {
                _gameInstance.SteamCmdPath = savedSteamCmdPath;
            }

            var savedSteamCmdCache = _configService.GetConfigValue(SteamCmdCacheKey);
            if (!string.IsNullOrEmpty(savedSteamCmdCache))
            {
                _gameInstance.SteamCmdAutoClearCache = bool.Parse(savedSteamCmdCache);
            }

            // Validate and fix any inconsistencies
            _gameInstance.ValidateAndFix();
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

            // Save Steam settings
            _configService.SetConfigValue(SteamInstallationKey, _gameInstance.IsSteamInstallation.ToString());
            _configService.SetConfigValue(SteamWorkshopFolderKey, _gameInstance.WorkshopFolder ?? string.Empty);
            _configService.SetConfigValue(SteamIntegrationKey, _gameInstance.SteamClientIntegration.ToString());
            _configService.SetConfigValue(SteamProtocolKey, _gameInstance.LaunchViaSteamProtocol.ToString());
            _configService.SetConfigValue(SteamCmdPathKey, _gameInstance.SteamCmdPath);
            _configService.SetConfigValue(SteamCmdCacheKey, _gameInstance.SteamCmdAutoClearCache.ToString());

            foreach (var row in Providers)
            {
                row.WriteBack();
            }

            _configService.SaveConfig();
            CloseDialog(true);
        }
    }
}