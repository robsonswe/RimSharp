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
    /// <summary>
    /// One editable row in the AI Providers tab: display name, API key and
    /// preferred model. The provider gets written back through the
    /// IAIProviderClient contract so any future provider plugs in the same
    /// way (config-backed key/model per provider).
    /// </summary>
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

    /// <summary>
    /// RimSharp settings: two tabs — Paths (game/config folders) and
    /// AI Providers (per-provider API key + model). Save persists
    /// everything through IConfigService; Cancel discards the edits.
    /// Returns <c>true</c> (DialogResult) when something was saved.
    /// </summary>
    public class SettingsDialogViewModel : DialogViewModelBase<bool>
    {
        private const string GameFolderKey = "game_folder";
        private const string ConfigFolderKey = "config_folder";

        private readonly IConfigService _configService;
        private readonly IFileDialogService? _fileDialogService;

        // Paths tab
        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }

        private string _configPath = string.Empty;
        public string ConfigPath { get => _configPath; set => SetProperty(ref _configPath, value); }

        // AI Providers tab
        public IReadOnlyList<AiProviderSettingsRow> Providers { get; }

        public ICommand BrowseGamePathCommand { get; }
        public ICommand BrowseConfigPathCommand { get; }
        public ICommand OpenGameFolderCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public SettingsDialogViewModel(
            IConfigService configService,
            IFileDialogService? fileDialogService,
            IEnumerable<IAIProviderClient>? providers)
        : base("RimSharp Settings")
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _fileDialogService = fileDialogService;

            _gamePath = _configService.GetConfigValue(GameFolderKey) ?? string.Empty;
            _configPath = _configService.GetConfigValue(ConfigFolderKey) ?? string.Empty;

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

            foreach (var row in Providers)
            {
                row.WriteBack();
            }

            _configService.SaveConfig();
            CloseDialog(true);
        }
    }
}
