#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.Infrastructure.Mods.Rules;
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

    /// <summary>
    /// One row of the "Rule Databases" settings section: an editable snapshot
    /// of a <see cref="ModRuleSourceRecord"/>. The dialog works on these rows
    /// and only writes back to the shared registry on Save.
    /// </summary>
    public class RuleSourceRowViewModel : ViewModelBase
    {
        private readonly IRuleSourceManager? _ruleSourceManager;
        private readonly IDialogService? _dialogService;
        private readonly IFileDialogService? _fileDialogService;

        public RuleSourceRowViewModel(
            ModRuleSourceRecord record,
            IRuleSourceManager? ruleSourceManager = null,
            IDialogService? dialogService = null,
            IFileDialogService? fileDialogService = null)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            _ruleSourceManager = ruleSourceManager;
            _dialogService = dialogService;
            _fileDialogService = fileDialogService;

            EditLocationCommand = CreateAsyncCommand(() => EditLocationAsync(this));
            UpdateNowCommand = CreateAsyncCommand(() => UpdateNowAsync(this));
        }

        public ModRuleSourceRecord Record { get; }

        public string Id => Record.Id;

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        private string _url = string.Empty;
        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value))
                {
                    OnPropertyChanged(nameof(Location));
                }
            }
        }

        private string _localPath = string.Empty;
        public string LocalPath
        {
            get => _localPath;
            set
            {
                if (SetProperty(ref _localPath, value))
                {
                    OnPropertyChanged(nameof(Location));
                }
            }
        }

        public bool IsRepoFile => Record.Type == ModRuleSourceType.RepoFile;
        public bool IsLocalFile => Record.Type == ModRuleSourceType.LocalFile;

        /// <summary>
        /// True for the curated RimSharpDB entry: its file is owned by the
        /// SHA-gated curated sync, so its URL is neither editable nor fetched
        /// by the registry sync.
        /// </summary>
        public bool IsCurated => string.Equals(Record.Id, ModRuleSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase);

        /// <summary>"Remote file" / "Local file" badge text.</summary>
        public string TypeBadge => IsLocalFile ? "Local file" : "Remote file";

        /// <summary>
        /// The URL (remote) or path (local) shown/edited in the row; writes are
        /// routed to the matching backing property. The curated entry shows a
        /// placeholder instead: its file is owned by the SHA-gated sync.
        /// </summary>
        public string Location
        {
            get => IsCurated
                ? "(synced with the curated RimSharpDB update)"
                : IsLocalFile ? LocalPath : Url;
            set
            {
                if (IsCurated) return;
                if (IsLocalFile) LocalPath = value; else Url = value;
            }
        }

        public ICommand EditLocationCommand { get; }
        public ICommand UpdateNowCommand { get; }

        private async Task EditLocationAsync(RuleSourceRowViewModel row)
        {
            if (row.IsLocalFile)
            {
                if (_fileDialogService == null) return;
                var (success, path) = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Select Rules File", "JSON files|*.json|All files|*.*", row.LocalPath);
                if (success && !string.IsNullOrWhiteSpace(path))
                {
                    row.LocalPath = path;
                    row.OnPropertyChanged(nameof(Location));
                }
                return;
            }

            if (_dialogService == null) return;
            var (result, input) = await _dialogService.ShowInputDialogAsync(
                "Rule Database URL",
                $"Raw file URL for '{row.Name}':",
                row.Url);
            if (result == MessageDialogResult.OK && !string.IsNullOrWhiteSpace(input))
            {
                row.Url = input.Trim();
                row.OnPropertyChanged(nameof(Location));
            }
        }

        private async Task UpdateNowAsync(RuleSourceRowViewModel row)
        {
            if (_ruleSourceManager == null) return;
            try
            {
                await _ruleSourceManager.RefreshSourceAsync(row.Id);
            }
            catch (Exception)
            {
                // RefreshSourceAsync already isolates per-source failures; surface
                // nothing extra here (the manager logs).
            }
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
        private readonly ISteamClaimValidator _steamClaimValidator;
        private readonly GameInstallationDetectionService _gameInstallationDetectionService;
        private readonly IDialogService? _dialogService;
        private readonly ILoggerService _logger;
        private readonly IRuleSourceManager? _ruleSourceManager;

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

        // Auto-sync model: an EMPTY backing field means "no override —
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
                        return _pathService.GetModsPathFor(_gamePath);
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
                            if (_modsFolderPath.Equals(_pathService.GetModsPathFor(_gamePath), StringComparison.OrdinalIgnoreCase))
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
                    OnPropertyChanged(nameof(IsManualSteamClaim));
                    OnPropertyChanged(nameof(ManualSteamClaimHint));
                    ApplyDistributionPaths(userInitiatedChange: true);
                }
            }
        }

        /// <summary>
        /// True when Steam is selected but no Steam candidate was auto-detected — the
        /// user is in "manual Steam confirmation" mode: paths are kept/edited by hand
        /// and the claim is validated on Save (or on demand, see
        /// ValidateSteamClaimCommand). Nothing silently re-points the paths.
        /// </summary>
        public bool IsManualSteamClaim =>
            SelectedDistribution == GameDistribution.Steam && SteamCandidate == null;

        public string ManualSteamClaimHint => IsManualSteamClaim
            ? "Steam was not auto-detected. Keep or set the Game Directory manually — it will be validated against Steam when you Save."
            : string.Empty;

        private string _manualSteamClaimStatus = string.Empty;
        /// <summary>Result line shown after a manual claim check (empty until one runs).</summary>
        public string ManualSteamClaimStatus
        {
            get => _manualSteamClaimStatus;
            private set => SetProperty(ref _manualSteamClaimStatus, value);
        }

        private GameInstallationCandidate? _steamCandidate;
        public GameInstallationCandidate? SteamCandidate
        {
            get => _steamCandidate;
            private set
            {
                if (SetProperty(ref _steamCandidate, value))
                {
                    OnPropertyChanged(nameof(SteamAvailable));
                    OnPropertyChanged(nameof(IsManualSteamClaim));
                    OnPropertyChanged(nameof(ManualSteamClaimHint));
                }
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
        public ICommand ValidateSteamClaimCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public IReadOnlyList<AiProviderSettingsRow> Providers { get; }
        public bool ShowAiProviders { get; }

        // --- Rule Databases section -----------------------------------------
        /// <summary>Editable snapshot of the rule source registry (priority order).</summary>
        public ObservableCollection<RuleSourceRowViewModel> RuleSources { get; } = new();

        private RuleSourceRowViewModel? _selectedRuleSource;
        public RuleSourceRowViewModel? SelectedRuleSource
        {
            get => _selectedRuleSource;
            set
            {
                if (SetProperty(ref _selectedRuleSource, value))
                {
                    OnPropertyChanged(nameof(CanRemoveRuleSource));
                    OnPropertyChanged(nameof(CanMoveRuleSourceUp));
                    OnPropertyChanged(nameof(CanMoveRuleSourceDown));
                }
            }
        }

        public bool CanRemoveRuleSource => SelectedRuleSource != null;
        public bool CanMoveRuleSourceUp => SelectedRuleSource != null && RuleSources.IndexOf(SelectedRuleSource) > 0;
        public bool CanMoveRuleSourceDown => SelectedRuleSource != null && RuleSources.IndexOf(SelectedRuleSource) >= 0 && RuleSources.IndexOf(SelectedRuleSource) < RuleSources.Count - 1;

        private string _ruleSourcesStatus = string.Empty;
        /// <summary>Status line under the rule source list ("rules as of ...", update results).</summary>
        public string RuleSourcesStatus
        {
            get => _ruleSourcesStatus;
            private set => SetProperty(ref _ruleSourcesStatus, value);
        }

        public ICommand AddRemoteRuleSourceCommand { get; }
        public ICommand AddLocalRuleSourceCommand { get; }
        public ICommand RemoveRuleSourceCommand { get; }
        public ICommand MoveRuleSourceUpCommand { get; }
        public ICommand MoveRuleSourceDownCommand { get; }
        public ICommand UpdateAllRuleSourcesCommand { get; }
        public ICommand ResetRuleSourcesCommand { get; }

        public SettingsDialogViewModel(
            IConfigService configService,
            IPathService pathService,
            IFileDialogService? fileDialogService,
            ILoggerService logger,
            IEnumerable<IAIProviderClient>? providers,
            ISteamClaimValidator steamClaimValidator,
            GameInstallationDetectionService gameInstallationDetectionService,
            IDialogService? dialogService = null,
            bool showAiProviders = true,
            IRuleSourceManager? ruleSourceManager = null)
        : base("RimSharp Settings")
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _fileDialogService = fileDialogService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dialogService = dialogService;
            _steamClaimValidator = steamClaimValidator ?? throw new ArgumentNullException(nameof(steamClaimValidator));
            _gameInstallationDetectionService = gameInstallationDetectionService ?? throw new ArgumentNullException(nameof(gameInstallationDetectionService));
            _ruleSourceManager = ruleSourceManager;
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
            ValidateSteamClaimCommand = CreateCommand(ValidateSteamClaimNow);
            SaveCommand = CreateCommand(Save);
            CancelCommand = CreateCommand(() => CloseDialog(false));

            // Rule Databases section: snapshot the registry into editable rows.
            LoadRuleSources();
            AddRemoteRuleSourceCommand = CreateAsyncCommand(AddRemoteRuleSourceAsync);
            AddLocalRuleSourceCommand = CreateAsyncCommand(AddLocalRuleSourceAsync);
            RemoveRuleSourceCommand = CreateCommand(RemoveRuleSource, () => CanRemoveRuleSource, nameof(SelectedRuleSource));
            MoveRuleSourceUpCommand = CreateCommand(MoveRuleSourceUp, () => CanMoveRuleSourceUp, nameof(SelectedRuleSource));
            MoveRuleSourceDownCommand = CreateCommand(MoveRuleSourceDown, () => CanMoveRuleSourceDown, nameof(SelectedRuleSource));
            UpdateAllRuleSourcesCommand = CreateAsyncCommand(UpdateAllRuleSourcesAsync);
            ResetRuleSourcesCommand = CreateCommand(ResetRuleSources);
        }

        /// <summary>
        /// Snapshots the shared registry into editable rows. The dialog edits
        /// the snapshot; the real registry is only written on Save.
        /// </summary>
        private void LoadRuleSources()
        {
            RuleSources.Clear();
            if (_ruleSourceManager == null) return;

            foreach (var record in _ruleSourceManager.Registry.Sources)
            {
                RuleSources.Add(CreateRow(record));
            }

            RefreshRuleSourcesStatus();
        }

        private RuleSourceRowViewModel CreateRow(ModRuleSourceRecord record)
        {
            var row = new RuleSourceRowViewModel(record, _ruleSourceManager, _dialogService, _fileDialogService)
            {
                Name = record.Name,
                Enabled = record.Enabled,
                Url = record.Url ?? string.Empty,
                LocalPath = record.LocalPath ?? string.Empty
            };
            return row;
        }

        private void RefreshRuleSourcesStatus()
        {
            if (_ruleSourceManager == null)
            {
                RuleSourcesStatus = string.Empty;
                return;
            }

            var timestamp = _ruleSourceManager.EffectiveTimestamp;
            var asOf = timestamp.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).ToLocalTime().ToString("d")
                : "unknown";
            RuleSourcesStatus = $"Rules as of {asOf}. Higher entries win when sources overlap.";
        }

        private async Task AddRemoteRuleSourceAsync()
        {
            if (_ruleSourceManager == null || _dialogService == null) return;

            var (result, input) = await _dialogService.ShowInputDialogAsync(
                "Add Rule Database",
                "Raw file URL of the rules JSON (e.g. a GitHub raw link):",
                "https://raw.githubusercontent.com/");
            if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(input)) return;

            var url = input.Trim();
            var id = BuildUniqueSourceId(Path.GetFileNameWithoutExtension(GetUrlPath(url)));
            RuleSources.Add(CreateRow(new ModRuleSourceRecord
            {
                Id = id,
                Name = id,
                Type = ModRuleSourceType.RepoFile,
                Url = url,
                Enabled = true
            }));
        }

        private async Task AddLocalRuleSourceAsync()
        {
            if (_ruleSourceManager == null || _fileDialogService == null) return;

            var (success, path) = await _fileDialogService.ShowOpenFileDialogAsync(
                "Add Local Rules File", "JSON files|*.json|All files|*.*");
            if (!success || string.IsNullOrWhiteSpace(path)) return;

            var id = BuildUniqueSourceId(Path.GetFileNameWithoutExtension(path));
            RuleSources.Add(CreateRow(new ModRuleSourceRecord
            {
                Id = id,
                Name = id,
                Type = ModRuleSourceType.LocalFile,
                LocalPath = path,
                Enabled = true
            }));
        }

        private void RemoveRuleSource()
        {
            if (SelectedRuleSource == null) return;
            RuleSources.Remove(SelectedRuleSource);
            SelectedRuleSource = null;
        }

        private void MoveRuleSourceUp()
        {
            if (SelectedRuleSource == null) return;
            var index = RuleSources.IndexOf(SelectedRuleSource);
            if (index <= 0) return;
            RuleSources.Move(index - 1, index);
            SelectedRuleSource = RuleSources[index - 1];
        }

        private void MoveRuleSourceDown()
        {
            if (SelectedRuleSource == null) return;
            var index = RuleSources.IndexOf(SelectedRuleSource);
            if (index < 0 || index >= RuleSources.Count - 1) return;
            RuleSources.Move(index, index + 1);
            SelectedRuleSource = RuleSources[index + 1];
        }

        private void ResetRuleSources()
        {
            if (_ruleSourceManager == null) return;
            RuleSources.Clear();
            foreach (var record in ModRuleSourceRegistry.CreateDefaults())
            {
                RuleSources.Add(CreateRow(record));
            }
        }

        private async Task UpdateAllRuleSourcesAsync()
        {
            if (_ruleSourceManager == null) return;

            RuleSourcesStatus = "Updating rule databases...";
            try
            {
                await _ruleSourceManager.RefreshAllAsync();
                RefreshRuleSourcesStatus();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Rule database update failed: {ex.Message}", nameof(SettingsDialogViewModel));
                RuleSourcesStatus = "Update failed. Check the URLs and your internet connection.";
            }
        }

        private static string GetUrlPath(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

        private string BuildUniqueSourceId(string baseId)
        {
            var id = string.IsNullOrWhiteSpace(baseId) ? "source" : baseId;
            var candidate = id;
            int suffix = 2;
            while (RuleSources.Any(r => string.Equals(r.Id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{id}-{suffix++}";
            }
            return candidate;
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
                    // Auto-fill mods path when game path is set — use PathService for bundle-aware derivation
                    ModsFolderPath = _pathService.GetModsPathFor(SteamCandidate.GameFolder);
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
                    // Auto-fill mods path when game path is set — use PathService for bundle-aware derivation
                    ModsFolderPath = _pathService.GetModsPathFor(NonSteamCandidate.GameFolder);
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

        /// <summary>
        /// "Check now" for manual Steam confirmation mode: validates the claimed Game
        /// Directory on demand and surfaces the result inline (status line) plus a
        /// message dialog. Same validation Save() will run — lets the user verify
        /// before committing anything.
        /// </summary>
        private async void ValidateSteamClaimNow()
        {
            if (!IsManualSteamClaim) return;

            var claim = await _steamClaimValidator.ValidateAsync(GamePath);
            if (claim.IsValid)
            {
                ManualSteamClaimStatus =
                    "✓ The Game Directory validates as a Steam-managed RimWorld installation." +
                    (claim.WorkshopFolder != null ? $" Workshop folder: {claim.WorkshopFolder}" : "");
                _logger.LogInfo(
                    $"Manual Steam claim validated for '{GamePath}'. Workshop folder: {claim.WorkshopFolder ?? "(auto-detect)"}",
                    nameof(SettingsDialogViewModel));

                if (_dialogService != null)
                {
                    _ = _dialogService.ShowInformation(
                        "Steam confirmed",
                        $"The Game Directory validates as a Steam-managed RimWorld installation.\n\n" +
                        (claim.WorkshopFolder != null
                            ? $"Workshop folder: {claim.WorkshopFolder}"
                            : "The workshop folder will be auto-detected."));
                }
            }
            else
            {
                ManualSteamClaimStatus = claim.FailureReason;
                LogAndShowSteamClaimRefusal(claim.FailureReason);
            }
        }

        private void LogAndShowSteamClaimRefusal(string failureReason)
        {
            _logger.LogWarning(
                $"Manual Steam claim rejected for '{GamePath}': {failureReason}",
                nameof(SettingsDialogViewModel));

            if (_dialogService != null)
            {
                // Fire-and-forget: the error surface is a separate message dialog owned
                // by the main window; the settings dialog stays open for corrections.
                _ = _dialogService.ShowError(
                    "Steam confirmation failed",
                    $"{failureReason}\n\n" +
                    "Fix the Game Directory above, or choose \"Non-Steam\" in Game Distribution.");
            }
        }

        internal void Save()
        {
            // Manual Steam confirmation: a Steam selection without a
            // detected candidate is only a claim until ValidateSteamClaim passes. Validate
            // up-front so a refusal writes NOTHING (not even the path updates below) —
            // the in-memory config stays exactly as it was when the dialog opened.
            string? manualSteamWorkshop = null;
            if (SelectedDistribution == GameDistribution.Steam && SteamCandidate == null)
            {
                var claim = _steamClaimValidator.ValidateAsync(GamePath).GetAwaiter().GetResult();
                if (!claim.IsValid)
                {
                    LogAndShowSteamClaimRefusal(claim.FailureReason);
                    return; // Refuse to persist anything
                }

                // Claim validated: use the resolved workshop folder only when it actually
                // exists on disk; otherwise persist auto-detect semantics (empty value
                // re-resolves on every launch).
                manualSteamWorkshop = claim.WorkshopFolder;
                _logger.LogInfo(
                    $"Manual Steam claim validated for '{GamePath}'. " +
                    $"Workshop folder: {claim.WorkshopFolder ?? "(auto-detect)"}",
                    nameof(SettingsDialogViewModel));
            }

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
                        isAutoSynced = ModsFolderPath.Equals(_pathService.GetModsPathFor(GamePath), StringComparison.OrdinalIgnoreCase);
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

            if (SelectedDistribution == GameDistribution.Steam)
            {
                if (SteamCandidate != null)
                {
                    // Auto-detected Steam install: trust ladder rung 1 + 2 already satisfied.
                    _configService.SetConfigValue(SteamInstallationKey, "true");
                    _configService.SetConfigValue(SteamWorkshopFolderKey, SteamCandidate.WorkshopFolder ?? string.Empty);
                    _configService.SetConfigValue(SteamIntegrationKey, SteamCandidate.HasSteamClientIntegration.ToString());
                }
                else
                {
                    // Manual claim already validated up-front (see top of Save).
                    _configService.SetConfigValue(SteamInstallationKey, "true");
                    _configService.SetConfigValue(SteamWorkshopFolderKey, manualSteamWorkshop ?? string.Empty);
                    _configService.SetConfigValue(SteamIntegrationKey, "true");
                }
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

            // Rule databases: write the edited snapshot back to the shared
            // registry, persist it and re-merge the rules onto the loaded mods
            // (live — a removed/disabled source's rules disappear without a
            // restart).
            if (_ruleSourceManager != null)
            {
                _ruleSourceManager.Registry.ReplaceAll(RuleSources.Select(row => new ModRuleSourceRecord
                {
                    Id = row.Record.Id,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? row.Record.Id : row.Name.Trim(),
                    Type = row.Record.Type,
                    Url = row.IsRepoFile ? (string.IsNullOrWhiteSpace(row.Url) ? null : row.Url.Trim()) : null,
                    LocalPath = row.IsLocalFile ? (string.IsNullOrWhiteSpace(row.LocalPath) ? null : row.LocalPath.Trim()) : null,
                    Enabled = row.Enabled,
                    Bundled = row.Record.Bundled
                }));
                _ruleSourceManager.CommitRegistryChanges();
            }

            _configService.SaveConfig();

            // Refresh path service to pick up the new mods_folder value
            _pathService.RefreshPaths();

            CloseDialog(true);
        }
    }
}