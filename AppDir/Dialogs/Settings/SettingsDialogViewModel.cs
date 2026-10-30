#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Services.Contracts;

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

        private readonly IConfigService _configService;
        private readonly IFileDialogService? _fileDialogService;

        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }

        private string _configPath = string.Empty;
        public string ConfigPath { get => _configPath; set => SetProperty(ref _configPath, value); }

        private string _updateChannel = "stable";
        public string UpdateChannel { get => _updateChannel; set => SetProperty(ref _updateChannel, value); }

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
            ShowAiProviders = showAiProviders;

            _gamePath = _configService.GetConfigValue(GameFolderKey);
            _configPath = _configService.GetConfigValue(ConfigFolderKey);

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

            foreach (var row in Providers)
            {
                row.WriteBack();
            }

            _configService.SaveConfig();
            CloseDialog(true);
        }
    }
}