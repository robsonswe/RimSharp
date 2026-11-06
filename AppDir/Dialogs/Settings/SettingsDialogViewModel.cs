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
        private const string ModsFolderKey = "mods_folder";

        private readonly IConfigService _configService;
        private readonly IPathService _pathService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly GameInstallationDetectionService _gameInstallationDetectionService;
        private readonly ILoggerService _logger;

        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set
        {
            if (SetProperty(ref _gamePath, value))
            {
                // Notify dependent computed properties so IsEnabled bindings re-evaluate
                OnPropertyChanged(nameof(IsGamePathSet));
                OnPropertyChanged(nameof(IsModsFolderEnabled));

                // When auto-synced (backing empty), the displayed value derives from the
                // game path — refresh the binding so it re-derives for the new path.
                if (string.IsNullOrWhiteSpace(_modsFolderPath))
                {
                    OnPropertyChanged(nameof(ModsFolderPath));
                }
            }
        }}

        private string _configPath = string.Empty;
        public string ConfigPath { get => _configPath; set => SetProperty(ref _configPath, value); }

        // Auto-sync model (handoff §2.3): an EMPTY backing field means "no override —
        // derive <game>\Mods", mirroring the runtime PathService semantics. The UI shows
        // the derived path so the user can see what resolves; anything the user types
        // other than the derived path becomes a persisted override. If the user sets the
        // field back to exactly <game>\Mods, the backing clears and we return to auto-sync.
        private string _modsFolderPath = string.Empty;
        public string ModsFolderPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_modsFolderPath) && !string.IsNullOrWhiteSpace(_gamePath))
                {
                    try
                    {
                        return Path.Combine(_gamePath, "Mods");
                    }
                    catch
                    {
                        return string.Empty; // Malformed game path — show nothing rather than throw
                    }
                }
                return _modsFolderPath;
            }
            set
            {
                if (SetProperty(ref _modsFolderPath, value))
                {
                    // Reverting to the derived path = returning to auto-sync: store the
                    // canonical "empty" (auto) state so runtime keeps deriving.
                    if (!string.IsNullOrWhiteSpace(_modsFolderPath) && !string.IsNullOrWhiteSpace(GamePath))
                    {
                        try
                        {
                            if (_modsFolderPath.Equals(Path.Combine(_gamePath, "Mods"), StringComparison.OrdinalIgnoreCase))
                            {
                                _modsFolderPath = string.Empty;
                                OnPropertyChanged(nameof(ModsFolderPath)); // getter now shows derived
                            }
                        }
                        catch
                        {
                            // Malformed game path — leave the typed value as a manual override
                        }
                    }
                }
            }
        }

        private string _resolvedModsPath = string.Empty;
        public string ResolvedModsPath => _pathService.GetGamePath() != string.Empty
            ? _pathService.GetModsPath()
            : "No game path set";

        public bool IsGamePathSet => !string.IsNullOrWhiteSpace(GamePath);

        public bool IsModsFolderEnabled => IsGamePathSet;

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
        public ICommand BrowseModsFolderCommand { get; }
        public ICommand OpenGameFolderCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand OpenModsFolderCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public IReadOnlyList<AiProviderSettingsRow> Providers { get; }
        public bool ShowAiProviders { get; }

        public SettingsDialogViewModel(
            IConfigService configService,
            IPathService pathService,
            IFileDialogService? fileDialogService,
            ILoggerService logger,
            IEnumerable<IAIProviderClient>? providers,
            bool showAiProviders = true)
        : base("RimSharp Settings")
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _fileDialogService = fileDialogService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameInstallationDetectionService = new GameInstallationDetectionService();
            ShowAiProviders = showAiProviders;

            _gamePath = _configService.GetConfigValue(GameFolderKey) ?? string.Empty;
            _configPath = _configService.GetConfigValue(ConfigFolderKey) ?? string.Empty;
            // Empty config value = auto mode; ModsFolderPath getter derives <game>\Mods live.

            var channel = _configService.GetConfigValue(UpdateChannelKey);
            _updateChannel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel;

            LoadSettings();

            Providers = (providers ?? Array.Empty<IAIProviderClient>())
                .Select(p => new AiProviderSettingsRow(p))
                .ToList();

            BrowseGamePathCommand = CreateAsyncCommand(BrowseGamePathAsync);
            BrowseConfigPathCommand = CreateAsyncCommand(BrowseConfigPathAsync);
            BrowseModsFolderCommand = CreateAsyncCommand(BrowseModsFolderAsync);
            OpenGameFolderCommand = CreateCommand(() => OpenFolder(GamePath));
            OpenConfigFolderCommand = CreateCommand(() => OpenFolder(ConfigPath));
            OpenModsFolderCommand = CreateCommand(() => OpenFolder(ModsFolderPath));
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
                    // Auto-fill mods path when game path is set
                    ModsFolderPath = Path.Combine(SteamCandidate.GameFolder, "Mods");
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
                    // Auto-fill mods path when game path is set
                    ModsFolderPath = Path.Combine(NonSteamCandidate.GameFolder, "Mods");
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

        private async Task BrowseModsFolderAsync() => await BrowseFolderAsync(
            "Select Mods Directory", ModsFolderPath, p => ModsFolderPath = p);

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

        internal void Save()
        {
            _configService.SetConfigValue(GameFolderKey, GamePath?.Trim() ?? string.Empty);
            _configService.SetConfigValue(ConfigFolderKey, ConfigPath?.Trim() ?? string.Empty);
            _configService.SetConfigValue(UpdateChannelKey, UpdateChannel);
            _configService.SetConfigValue(GameDistributionKey, SelectedDistribution.ToString());

            // mods_folder canonical semantics (match PathService): EMPTY = auto-derive
            // <game>\Mods; a different value = a persisted override (warn + keep if broken).
            if (!string.IsNullOrWhiteSpace(ModsFolderPath))
            {
                var isAutoSynced = false;
                if (!string.IsNullOrWhiteSpace(GamePath))
                {
                    try
                    {
                        isAutoSynced = ModsFolderPath.Equals(Path.Combine(GamePath, "Mods"), StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        isAutoSynced = false; // Malformed game path — treat as a manual override
                    }
                }

                if (isAutoSynced)
                {
                    // In sync with <game>\Mods: persist the canonical EMPTY (auto) value so
                    // runtime keeps deriving if the game path ever changes outside this dialog.
                    _configService.SetConfigValue(ModsFolderKey, string.Empty);
                }
                else if (!Directory.Exists(ModsFolderPath))
                {
                    _logger.LogWarning($"Mods folder override '{ModsFolderPath}' does not exist. Falling back to automatic path.", nameof(SettingsDialogViewModel));
                    // Don't clear the stored value — the user may fix it later
                    // Save the invalid value anyway (warn + allow save, never clear)
                    _configService.SetConfigValue(ModsFolderKey, ModsFolderPath.Trim());
                }
                else
                {
                    _configService.SetConfigValue(ModsFolderKey, ModsFolderPath.Trim());
                }
            }
            else
            {
                // Clear the override if user clears the field
                _configService.SetConfigValue(ModsFolderKey, string.Empty);
            }

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

            // Refresh path service to pick up the new mods_folder value
            _pathService.RefreshPaths();

            CloseDialog(true);
        }
    }
}