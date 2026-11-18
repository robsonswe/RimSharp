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
    /// One row of an external sources settings section: an editable snapshot
    /// of a <see cref="ModRuleSourceRecord"/>. The dialog works on these rows
    /// and only writes back to the shared registry on Save.
    /// </summary>
    public class RuleSourceRowViewModel : ViewModelBase
    {
        private readonly IExternalSourceManager? _sourceManager;
        private readonly IDialogService? _dialogService;
        private readonly IFileDialogService? _fileDialogService;

        public RuleSourceRowViewModel(
            ModRuleSourceRecord record,
            IExternalSourceManager? sourceManager = null,
            IDialogService? dialogService = null,
            IFileDialogService? fileDialogService = null)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            _sourceManager = sourceManager;
            _dialogService = dialogService;
            _fileDialogService = fileDialogService;

            EditLocationCommand = CreateAsyncCommand(() => EditLocationAsync(this));
            UpdateNowCommand = CreateAsyncCommand(() => UpdateNowAsync(this));
            ToggleEnabledCommand = CreateCommand(() => Enabled = !Enabled);
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
        public bool IsMod => Record.Type == ModRuleSourceType.Mod;

        /// <summary>
        /// True for the curated RimSharpDB entry: its file is owned by the
        /// SHA-gated curated sync, so its URL is neither editable nor fetched
        /// by the registry sync.
        /// </summary>
        public bool IsCurated => string.Equals(Record.Id, ModRuleSourceRegistry.CuratedSourceId, StringComparison.OrdinalIgnoreCase);

        /// <summary>"built-in" / "mod" / "Local file" / "Remote file" badge text.</summary>
        public string TypeBadge
        {
            get
            {
                if (IsCurated) return "built-in";
                if (IsMod) return "mod";
                if (IsLocalFile) return "Local file";
                return "Remote file";
            }
        }

        /// <summary>True when this source is locked (built-in curated RimSharpDB or installed mod).</summary>
        public bool Locked => Record.Locked;

        /// <summary>"built-in" or "mod" badge text shown on locked rows.</summary>
        public string LockedBadge => IsCurated ? "built-in" : (IsMod ? "mod" : string.Empty);

        public bool CanUpdate => IsRepoFile && !string.IsNullOrWhiteSpace(Url) && !IsCurated;

        /// <summary>
        /// Per-source "rules as of" date (short date) derived from that
        /// source's upstream timestamp, or empty when the source provides none.
        /// </summary>
        public string RulesAsOf
        {
            get
            {
                if (_sourceManager == null) return string.Empty;
                if (!_sourceManager.SourceTimestamps.TryGetValue(Record.Id, out var ts) || ts <= 0)
                {
                    return string.Empty;
                }
                return DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("d");
            }
        }

        /// <summary>
        /// The URL (remote) or path (local) shown/edited in the row; writes are
        /// routed to the matching backing property. Curated and mod entries show
        /// a placeholder instead.
        /// </summary>
        public string Location
        {
            get => (IsCurated || IsMod)
                ? string.Empty
                : IsLocalFile ? LocalPath : Url;
            set
            {
                if (IsCurated || IsMod) return;
                if (IsLocalFile) LocalPath = value; else Url = value;
            }
        }

        public ICommand EditLocationCommand { get; }
        public ICommand UpdateNowCommand { get; }
        public ICommand ToggleEnabledCommand { get; }

        private async Task EditLocationAsync(RuleSourceRowViewModel row)
        {
            if (row.Locked) return;

            if (row.IsLocalFile)
            {
                if (_fileDialogService == null) return;
                var (success, path) = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Select Source File", "JSON files|*.json|All files|*.*", row.LocalPath);
                if (success && !string.IsNullOrWhiteSpace(path))
                {
                    row.LocalPath = path;
                    row.OnPropertyChanged(nameof(Location));
                }
                return;
            }

            if (_dialogService == null) return;
            var (result, name, url) = await _dialogService.ShowRuleSourceEditorDialogAsync(
                "Edit Source",
                $"Edit the name and URL for '{row.Name}':",
                row.Name,
                row.Url);
            if (result == MessageDialogResult.OK)
            {
                if (!string.IsNullOrWhiteSpace(name)) row.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    row.Url = url.Trim();
                    row.OnPropertyChanged(nameof(Location));
                }
            }
        }

        private async Task UpdateNowAsync(RuleSourceRowViewModel row)
        {
            if (_sourceManager == null || !row.CanUpdate) return;
            try
            {
                await _sourceManager.RefreshSourceAsync(row.Id);
            }
            catch (Exception)
            {
                // RefreshSourceAsync already isolates per-source failures.
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
        private readonly Shared.Services.Contracts.IModInfoSourceManager? _modInfoSourceManager;
        private readonly Shared.Services.Contracts.IModReplacementSourceManager? _replacementSourceManager;
        private readonly Shared.Services.Contracts.INoVersionWarningSourceManager? _noVersionWarningSourceManager;

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

        public bool CanRemoveRuleSource => SelectedRuleSource != null && !SelectedRuleSource.Locked;
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
        public ICommand RemoveRuleSourceCommand { get; }
        public ICommand MoveRuleSourceUpCommand { get; }
        public ICommand MoveRuleSourceDownCommand { get; }
        public ICommand UpdateAllRuleSourcesCommand { get; }
        public ICommand ResetRuleSourcesCommand { get; }

        // --- ModInfo (Dictionary) Databases section --------------------------
        public ObservableCollection<RuleSourceRowViewModel> ModInfoSources { get; } = new();

        private RuleSourceRowViewModel? _selectedModInfoSource;
        public RuleSourceRowViewModel? SelectedModInfoSource
        {
            get => _selectedModInfoSource;
            set
            {
                if (SetProperty(ref _selectedModInfoSource, value))
                {
                    OnPropertyChanged(nameof(CanRemoveModInfoSource));
                    OnPropertyChanged(nameof(CanMoveModInfoSourceUp));
                    OnPropertyChanged(nameof(CanMoveModInfoSourceDown));
                }
            }
        }

        public bool CanRemoveModInfoSource => SelectedModInfoSource != null && !SelectedModInfoSource.Locked;
        public bool CanMoveModInfoSourceUp => SelectedModInfoSource != null && ModInfoSources.IndexOf(SelectedModInfoSource) > 0;
        public bool CanMoveModInfoSourceDown => SelectedModInfoSource != null && ModInfoSources.IndexOf(SelectedModInfoSource) >= 0 && ModInfoSources.IndexOf(SelectedModInfoSource) < ModInfoSources.Count - 1;

        private string _modInfoSourcesStatus = string.Empty;
        public string ModInfoSourcesStatus
        {
            get => _modInfoSourcesStatus;
            private set => SetProperty(ref _modInfoSourcesStatus, value);
        }

        public ICommand AddRemoteModInfoSourceCommand { get; }
        public ICommand RemoveModInfoSourceCommand { get; }
        public ICommand MoveModInfoSourceUpCommand { get; }
        public ICommand MoveModInfoSourceDownCommand { get; }
        public ICommand UpdateAllModInfoSourcesCommand { get; }
        public ICommand ResetModInfoSourcesCommand { get; }

        // --- Replacements Databases section ---------------------------------
        public ObservableCollection<RuleSourceRowViewModel> ReplacementSources { get; } = new();

        private RuleSourceRowViewModel? _selectedReplacementSource;
        public RuleSourceRowViewModel? SelectedReplacementSource
        {
            get => _selectedReplacementSource;
            set
            {
                if (SetProperty(ref _selectedReplacementSource, value))
                {
                    OnPropertyChanged(nameof(CanRemoveReplacementSource));
                    OnPropertyChanged(nameof(CanMoveReplacementSourceUp));
                    OnPropertyChanged(nameof(CanMoveReplacementSourceDown));
                }
            }
        }

        public bool CanRemoveReplacementSource => SelectedReplacementSource != null && !SelectedReplacementSource.Locked;
        public bool CanMoveReplacementSourceUp => SelectedReplacementSource != null && ReplacementSources.IndexOf(SelectedReplacementSource) > 0;
        public bool CanMoveReplacementSourceDown => SelectedReplacementSource != null && ReplacementSources.IndexOf(SelectedReplacementSource) >= 0 && ReplacementSources.IndexOf(SelectedReplacementSource) < ReplacementSources.Count - 1;

        private string _replacementSourcesStatus = string.Empty;
        public string ReplacementSourcesStatus
        {
            get => _replacementSourcesStatus;
            private set => SetProperty(ref _replacementSourcesStatus, value);
        }

        public ICommand AddRemoteReplacementSourceCommand { get; }
        public ICommand RemoveReplacementSourceCommand { get; }
        public ICommand MoveReplacementSourceUpCommand { get; }
        public ICommand MoveReplacementSourceDownCommand { get; }
        public ICommand UpdateAllReplacementSourcesCommand { get; }
        public ICommand ResetReplacementSourcesCommand { get; }

        // --- NoVersionWarning Databases section ------------------------------
        public ObservableCollection<RuleSourceRowViewModel> NoVersionWarningSources { get; } = new();

        private RuleSourceRowViewModel? _selectedNoVersionWarningSource;
        public RuleSourceRowViewModel? SelectedNoVersionWarningSource
        {
            get => _selectedNoVersionWarningSource;
            set
            {
                if (SetProperty(ref _selectedNoVersionWarningSource, value))
                {
                    OnPropertyChanged(nameof(CanRemoveNoVersionWarningSource));
                    OnPropertyChanged(nameof(CanMoveNoVersionWarningSourceUp));
                    OnPropertyChanged(nameof(CanMoveNoVersionWarningSourceDown));
                }
            }
        }

        public bool CanRemoveNoVersionWarningSource => SelectedNoVersionWarningSource != null && !SelectedNoVersionWarningSource.Locked;
        public bool CanMoveNoVersionWarningSourceUp => SelectedNoVersionWarningSource != null && NoVersionWarningSources.IndexOf(SelectedNoVersionWarningSource) > 0;
        public bool CanMoveNoVersionWarningSourceDown => SelectedNoVersionWarningSource != null && NoVersionWarningSources.IndexOf(SelectedNoVersionWarningSource) >= 0 && NoVersionWarningSources.IndexOf(SelectedNoVersionWarningSource) < NoVersionWarningSources.Count - 1;

        private string _noVersionWarningSourcesStatus = string.Empty;
        public string NoVersionWarningSourcesStatus
        {
            get => _noVersionWarningSourcesStatus;
            private set => SetProperty(ref _noVersionWarningSourcesStatus, value);
        }

        public ICommand AddRemoteNoVersionWarningSourceCommand { get; }
        public ICommand RemoveNoVersionWarningSourceCommand { get; }
        public ICommand MoveNoVersionWarningSourceUpCommand { get; }
        public ICommand MoveNoVersionWarningSourceDownCommand { get; }
        public ICommand UpdateAllNoVersionWarningSourcesCommand { get; }
        public ICommand ResetNoVersionWarningSourcesCommand { get; }

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
            IRuleSourceManager? ruleSourceManager = null,
            Shared.Services.Contracts.IModInfoSourceManager? modInfoSourceManager = null,
            Shared.Services.Contracts.IModReplacementSourceManager? replacementSourceManager = null,
            Shared.Services.Contracts.INoVersionWarningSourceManager? noVersionWarningSourceManager = null)
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
            _modInfoSourceManager = modInfoSourceManager;
            _replacementSourceManager = replacementSourceManager;
            _noVersionWarningSourceManager = noVersionWarningSourceManager;
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
            RemoveRuleSourceCommand = CreateCommand<RuleSourceRowViewModel>(RemoveRuleSource, r =>
            {
                var t = r ?? SelectedRuleSource;
                return t != null && !t.Locked;
            }, nameof(SelectedRuleSource));
            MoveRuleSourceUpCommand = CreateCommand<RuleSourceRowViewModel>(MoveRuleSourceUp, r =>
            {
                var t = r ?? SelectedRuleSource;
                return t != null && RuleSources.IndexOf(t) > 0;
            }, nameof(SelectedRuleSource));
            MoveRuleSourceDownCommand = CreateCommand<RuleSourceRowViewModel>(MoveRuleSourceDown, r =>
            {
                var t = r ?? SelectedRuleSource;
                return t != null && RuleSources.IndexOf(t) >= 0 && RuleSources.IndexOf(t) < RuleSources.Count - 1;
            }, nameof(SelectedRuleSource));
            UpdateAllRuleSourcesCommand = CreateAsyncCommand(UpdateAllRuleSourcesAsync);
            ResetRuleSourcesCommand = CreateCommand(ResetRuleSources);

            // ModInfo Databases section
            LoadModInfoSources();
            AddRemoteModInfoSourceCommand = CreateAsyncCommand(AddRemoteModInfoSourceAsync);
            RemoveModInfoSourceCommand = CreateCommand<RuleSourceRowViewModel>(RemoveModInfoSource, r =>
            {
                var t = r ?? SelectedModInfoSource;
                return t != null && !t.Locked;
            }, nameof(SelectedModInfoSource));
            MoveModInfoSourceUpCommand = CreateCommand<RuleSourceRowViewModel>(MoveModInfoSourceUp, r =>
            {
                var t = r ?? SelectedModInfoSource;
                return t != null && ModInfoSources.IndexOf(t) > 0;
            }, nameof(SelectedModInfoSource));
            MoveModInfoSourceDownCommand = CreateCommand<RuleSourceRowViewModel>(MoveModInfoSourceDown, r =>
            {
                var t = r ?? SelectedModInfoSource;
                return t != null && ModInfoSources.IndexOf(t) >= 0 && ModInfoSources.IndexOf(t) < ModInfoSources.Count - 1;
            }, nameof(SelectedModInfoSource));
            UpdateAllModInfoSourcesCommand = CreateAsyncCommand(UpdateAllModInfoSourcesAsync);
            ResetModInfoSourcesCommand = CreateCommand(ResetModInfoSources);

            // Replacements Databases section
            LoadReplacementSources();
            AddRemoteReplacementSourceCommand = CreateAsyncCommand(AddRemoteReplacementSourceAsync);
            RemoveReplacementSourceCommand = CreateCommand<RuleSourceRowViewModel>(RemoveReplacementSource, r =>
            {
                var t = r ?? SelectedReplacementSource;
                return t != null && !t.Locked;
            }, nameof(SelectedReplacementSource));
            MoveReplacementSourceUpCommand = CreateCommand<RuleSourceRowViewModel>(MoveReplacementSourceUp, r =>
            {
                var t = r ?? SelectedReplacementSource;
                return t != null && ReplacementSources.IndexOf(t) > 0;
            }, nameof(SelectedReplacementSource));
            MoveReplacementSourceDownCommand = CreateCommand<RuleSourceRowViewModel>(MoveReplacementSourceDown, r =>
            {
                var t = r ?? SelectedReplacementSource;
                return t != null && ReplacementSources.IndexOf(t) >= 0 && ReplacementSources.IndexOf(t) < ReplacementSources.Count - 1;
            }, nameof(SelectedReplacementSource));
            UpdateAllReplacementSourcesCommand = CreateAsyncCommand(UpdateAllReplacementSourcesAsync);
            ResetReplacementSourcesCommand = CreateCommand(ResetReplacementSources);

            // NoVersionWarning Databases section
            LoadNoVersionWarningSources();
            AddRemoteNoVersionWarningSourceCommand = CreateAsyncCommand(AddRemoteNoVersionWarningSourceAsync);
            RemoveNoVersionWarningSourceCommand = CreateCommand<RuleSourceRowViewModel>(RemoveNoVersionWarningSource, r =>
            {
                var t = r ?? SelectedNoVersionWarningSource;
                return t != null && !t.Locked;
            }, nameof(SelectedNoVersionWarningSource));
            MoveNoVersionWarningSourceUpCommand = CreateCommand<RuleSourceRowViewModel>(MoveNoVersionWarningSourceUp, r =>
            {
                var t = r ?? SelectedNoVersionWarningSource;
                return t != null && NoVersionWarningSources.IndexOf(t) > 0;
            }, nameof(SelectedNoVersionWarningSource));
            MoveNoVersionWarningSourceDownCommand = CreateCommand<RuleSourceRowViewModel>(MoveNoVersionWarningSourceDown, r =>
            {
                var t = r ?? SelectedNoVersionWarningSource;
                return t != null && NoVersionWarningSources.IndexOf(t) >= 0 && NoVersionWarningSources.IndexOf(t) < NoVersionWarningSources.Count - 1;
            }, nameof(SelectedNoVersionWarningSource));
            UpdateAllNoVersionWarningSourcesCommand = CreateAsyncCommand(UpdateAllNoVersionWarningSourcesAsync);
            ResetNoVersionWarningSourcesCommand = CreateCommand(ResetNoVersionWarningSources);
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

        private RuleSourceRowViewModel CreateRow(ModRuleSourceRecord record, IExternalSourceManager? manager = null)
        {
            var row = new RuleSourceRowViewModel(record, manager ?? _ruleSourceManager, _dialogService, _fileDialogService)
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

            var (result, name, url) = await _dialogService.ShowRuleSourceEditorDialogAsync(
                "Add Rule Database",
                "Enter a display name and the raw file URL of the rules JSON (e.g. a GitHub raw link).",
                "My Rule Database",
                "https://raw.githubusercontent.com/");
            if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(url)) return;

            url = url.Trim();
            var id = BuildUniqueSourceId(Path.GetFileNameWithoutExtension(GetUrlPath(url)), RuleSources);
            var displayName = string.IsNullOrWhiteSpace(name) ? id : name.Trim();

            RuleSources.Add(CreateRow(new ModRuleSourceRecord
            {
                Id = id,
                Name = displayName,
                Type = ModRuleSourceType.RepoFile,
                Url = url,
                Enabled = true
            }, _ruleSourceManager));
        }

        private void RemoveRuleSource(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedRuleSource;
            if (target == null || target.Locked) return;
            RuleSources.Remove(target);
            SelectedRuleSource = null;
        }

        private void MoveRuleSourceUp(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedRuleSource;
            if (target == null) return;
            var index = RuleSources.IndexOf(target);
            if (index <= 0) return;
            RuleSources.Move(index - 1, index);
            SelectedRuleSource = RuleSources[index - 1];
        }

        private void MoveRuleSourceDown(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedRuleSource;
            if (target == null) return;
            var index = RuleSources.IndexOf(target);
            if (index < 0 || index >= RuleSources.Count - 1) return;
            RuleSources.Move(index, index + 1);
            SelectedRuleSource = RuleSources[index + 1];
        }

        private void ResetRuleSources()
        {
            if (_ruleSourceManager == null) return;
            _ruleSourceManager.Registry.ResetToDefaults();
            LoadRuleSources();
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

        // --- ModInfo (Dictionary) handlers ----------------------------------
        private void LoadModInfoSources()
        {
            ModInfoSources.Clear();
            if (_modInfoSourceManager == null) return;

            foreach (var record in _modInfoSourceManager.Registry.Sources)
            {
                ModInfoSources.Add(CreateRow(record, _modInfoSourceManager));
            }

            RefreshModInfoSourcesStatus();
        }

        private void RefreshModInfoSourcesStatus()
        {
            if (_modInfoSourceManager == null)
            {
                ModInfoSourcesStatus = string.Empty;
                return;
            }

            var ts = _modInfoSourceManager.EffectiveTimestamp;
            var asOf = ts.HasValue && ts.Value > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ts.Value).ToLocalTime().ToString("d")
                : null;

            ModInfoSourcesStatus = asOf != null
                ? $"Mod info as of {asOf}. First source with details for a mod wins."
                : "Mod info databases are resolved in priority order: first match wins.";
        }

        private async Task AddRemoteModInfoSourceAsync()
        {
            if (_modInfoSourceManager == null || _dialogService == null) return;

            var (result, name, url) = await _dialogService.ShowRuleSourceEditorDialogAsync(
                "Add Mod Info Database",
                "Enter a display name and the raw file URL of the mod info db.json (e.g. a GitHub raw link).",
                "My Mod Info Database",
                "https://raw.githubusercontent.com/");
            if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(url)) return;

            url = url.Trim();
            var id = BuildUniqueSourceId(Path.GetFileNameWithoutExtension(GetUrlPath(url)), ModInfoSources);
            var displayName = string.IsNullOrWhiteSpace(name) ? id : name.Trim();

            ModInfoSources.Add(CreateRow(new ModRuleSourceRecord
            {
                Id = id,
                Name = displayName,
                Type = ModRuleSourceType.RepoFile,
                Url = url,
                Enabled = true
            }, _modInfoSourceManager));
        }

        private void RemoveModInfoSource(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedModInfoSource;
            if (target == null || target.Locked) return;
            ModInfoSources.Remove(target);
            SelectedModInfoSource = null;
        }

        private void MoveModInfoSourceUp(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedModInfoSource;
            if (target == null) return;
            var index = ModInfoSources.IndexOf(target);
            if (index <= 0) return;
            ModInfoSources.Move(index - 1, index);
            SelectedModInfoSource = ModInfoSources[index - 1];
        }

        private void MoveModInfoSourceDown(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedModInfoSource;
            if (target == null) return;
            var index = ModInfoSources.IndexOf(target);
            if (index < 0 || index >= ModInfoSources.Count - 1) return;
            ModInfoSources.Move(index, index + 1);
            SelectedModInfoSource = ModInfoSources[index + 1];
        }

        private void ResetModInfoSources()
        {
            if (_modInfoSourceManager == null) return;
            _modInfoSourceManager.Registry.ResetToDefaults();
            LoadModInfoSources();
        }

        private async Task UpdateAllModInfoSourcesAsync()
        {
            if (_modInfoSourceManager == null) return;

            ModInfoSourcesStatus = "Updating mod info databases...";
            try
            {
                await _modInfoSourceManager.RefreshAllAsync();
                RefreshModInfoSourcesStatus();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Mod info database update failed: {ex.Message}", nameof(SettingsDialogViewModel));
                ModInfoSourcesStatus = "Update failed. Check the URLs and your internet connection.";
            }
        }

        // --- Replacements handlers ------------------------------------------
        private void LoadReplacementSources()
        {
            ReplacementSources.Clear();
            if (_replacementSourceManager == null) return;

            foreach (var record in _replacementSourceManager.Registry.Sources)
            {
                if (record.Type == ModRuleSourceType.Mod && !_replacementSourceManager.IsModInstalled(record.ModFolderId))
                {
                    continue;
                }
                ReplacementSources.Add(CreateRow(record, _replacementSourceManager));
            }

            RefreshReplacementSourcesStatus();
        }

        private void RefreshReplacementSourcesStatus()
        {
            if (_replacementSourceManager == null)
            {
                ReplacementSourcesStatus = string.Empty;
                return;
            }

            var ts = _replacementSourceManager.EffectiveTimestamp;
            var asOf = ts.HasValue && ts.Value > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ts.Value).ToLocalTime().ToString("d")
                : null;

            ReplacementSourcesStatus = asOf != null
                ? $"Replacements as of {asOf}. Higher entries win when sources overlap."
                : "Replacements are resolved in priority order: first match wins.";
        }

        private async Task AddRemoteReplacementSourceAsync()
        {
            if (_replacementSourceManager == null || _dialogService == null) return;

            var (result, name, url) = await _dialogService.ShowRuleSourceEditorDialogAsync(
                "Add Replacement Database",
                "Enter a display name and the raw file URL of the replacements JSON or JSON.GZ.",
                "My Replacement Database",
                "https://raw.githubusercontent.com/");
            if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(url)) return;

            url = url.Trim();
            var id = BuildUniqueSourceId(Path.GetFileNameWithoutExtension(GetUrlPath(url)), ReplacementSources);
            var displayName = string.IsNullOrWhiteSpace(name) ? id : name.Trim();

            ReplacementSources.Add(CreateRow(new ModRuleSourceRecord
            {
                Id = id,
                Name = displayName,
                Type = ModRuleSourceType.RepoFile,
                Url = url,
                Enabled = true
            }, _replacementSourceManager));
        }

        private void RemoveReplacementSource(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedReplacementSource;
            if (target == null || target.Locked) return;
            ReplacementSources.Remove(target);
            SelectedReplacementSource = null;
        }

        private void MoveReplacementSourceUp(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedReplacementSource;
            if (target == null) return;
            var index = ReplacementSources.IndexOf(target);
            if (index <= 0) return;
            ReplacementSources.Move(index - 1, index);
            SelectedReplacementSource = ReplacementSources[index - 1];
        }

        private void MoveReplacementSourceDown(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedReplacementSource;
            if (target == null) return;
            var index = ReplacementSources.IndexOf(target);
            if (index < 0 || index >= ReplacementSources.Count - 1) return;
            ReplacementSources.Move(index, index + 1);
            SelectedReplacementSource = ReplacementSources[index + 1];
        }

        private void ResetReplacementSources()
        {
            if (_replacementSourceManager == null) return;
            _replacementSourceManager.Registry.ResetToDefaults();
            LoadReplacementSources();
        }

        private async Task UpdateAllReplacementSourcesAsync()
        {
            if (_replacementSourceManager == null) return;

            ReplacementSourcesStatus = "Updating replacement databases...";
            try
            {
                await _replacementSourceManager.RefreshAllAsync();
                RefreshReplacementSourcesStatus();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Replacement database update failed: {ex.Message}", nameof(SettingsDialogViewModel));
                ReplacementSourcesStatus = "Update failed. Check the URLs and your internet connection.";
            }
        }

        // --- NoVersionWarning handlers --------------------------------------
        private void LoadNoVersionWarningSources()
        {
            NoVersionWarningSources.Clear();
            if (_noVersionWarningSourceManager == null) return;

            foreach (var record in _noVersionWarningSourceManager.Registry.Sources)
            {
                if (record.Type == ModRuleSourceType.Mod && !_noVersionWarningSourceManager.IsModInstalled(record.ModFolderId))
                {
                    continue;
                }
                NoVersionWarningSources.Add(CreateRow(record, _noVersionWarningSourceManager));
            }

            RefreshNoVersionWarningSourcesStatus();
        }

        private void RefreshNoVersionWarningSourcesStatus()
        {
            if (_noVersionWarningSourceManager == null)
            {
                NoVersionWarningSourcesStatus = string.Empty;
                return;
            }

            var ts = _noVersionWarningSourceManager.EffectiveTimestamp;
            var asOf = ts.HasValue && ts.Value > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ts.Value).ToLocalTime().ToString("d")
                : null;

            NoVersionWarningSourcesStatus = asOf != null
                ? $"Version compatibility as of {asOf}. Higher entries win when sources overlap."
                : "Version compatibility allowlists are combined across all enabled sources.";
        }

        private async Task AddRemoteNoVersionWarningSourceAsync()
        {
            if (_noVersionWarningSourceManager == null || _dialogService == null) return;

            var (result, name, url) = await _dialogService.ShowRuleSourceEditorDialogAsync(
                "Add Version Warning Database",
                "Enter a display name and the base raw URL of the repository containing version folders.",
                "My Version Warning Database",
                "https://raw.githubusercontent.com/");
            if (result != MessageDialogResult.OK || string.IsNullOrWhiteSpace(url)) return;

            url = url.Trim();
            var id = BuildUniqueSourceId(Path.GetFileNameWithoutExtension(GetUrlPath(url)), NoVersionWarningSources);
            var displayName = string.IsNullOrWhiteSpace(name) ? id : name.Trim();

            NoVersionWarningSources.Add(CreateRow(new ModRuleSourceRecord
            {
                Id = id,
                Name = displayName,
                Type = ModRuleSourceType.RepoFile,
                Url = url,
                Enabled = true
            }, _noVersionWarningSourceManager));
        }

        private void RemoveNoVersionWarningSource(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedNoVersionWarningSource;
            if (target == null || target.Locked) return;
            NoVersionWarningSources.Remove(target);
            SelectedNoVersionWarningSource = null;
        }

        private void MoveNoVersionWarningSourceUp(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedNoVersionWarningSource;
            if (target == null) return;
            var index = NoVersionWarningSources.IndexOf(target);
            if (index <= 0) return;
            NoVersionWarningSources.Move(index - 1, index);
            SelectedNoVersionWarningSource = NoVersionWarningSources[index - 1];
        }

        private void MoveNoVersionWarningSourceDown(RuleSourceRowViewModel? row)
        {
            var target = row ?? SelectedNoVersionWarningSource;
            if (target == null) return;
            var index = NoVersionWarningSources.IndexOf(target);
            if (index < 0 || index >= NoVersionWarningSources.Count - 1) return;
            NoVersionWarningSources.Move(index, index + 1);
            SelectedNoVersionWarningSource = NoVersionWarningSources[index + 1];
        }

        private void ResetNoVersionWarningSources()
        {
            if (_noVersionWarningSourceManager == null) return;
            _noVersionWarningSourceManager.Registry.ResetToDefaults();
            LoadNoVersionWarningSources();
        }

        private async Task UpdateAllNoVersionWarningSourcesAsync()
        {
            if (_noVersionWarningSourceManager == null) return;

            NoVersionWarningSourcesStatus = "Updating version warning databases...";
            try
            {
                await _noVersionWarningSourceManager.RefreshAllAsync();
                RefreshNoVersionWarningSourcesStatus();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Version warning database update failed: {ex.Message}", nameof(SettingsDialogViewModel));
                NoVersionWarningSourcesStatus = "Update failed. Check the URLs and your internet connection.";
            }
        }

        private static string GetUrlPath(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

        private static string BuildUniqueSourceId(string baseId, IEnumerable<RuleSourceRowViewModel> existing)
        {
            var id = string.IsNullOrWhiteSpace(baseId) ? "source" : baseId;
            var candidate = id;
            int suffix = 2;
            while (existing.Any(r => string.Equals(r.Id, candidate, StringComparison.OrdinalIgnoreCase)))
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

            // External source registries: write snapshots back and commit changes
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

            if (_modInfoSourceManager != null)
            {
                _modInfoSourceManager.Registry.ReplaceAll(ModInfoSources.Select(row => new ModRuleSourceRecord
                {
                    Id = row.Record.Id,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? row.Record.Id : row.Name.Trim(),
                    Type = row.Record.Type,
                    Url = row.IsRepoFile ? (string.IsNullOrWhiteSpace(row.Url) ? null : row.Url.Trim()) : null,
                    LocalPath = row.IsLocalFile ? (string.IsNullOrWhiteSpace(row.LocalPath) ? null : row.LocalPath.Trim()) : null,
                    Enabled = row.Enabled,
                    Bundled = row.Record.Bundled
                }));
                _modInfoSourceManager.CommitRegistryChanges();
            }

            if (_replacementSourceManager != null)
            {
                _replacementSourceManager.Registry.ReplaceAll(ReplacementSources.Select(row => new ModRuleSourceRecord
                {
                    Id = row.Record.Id,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? row.Record.Id : row.Name.Trim(),
                    Type = row.Record.Type,
                    Url = row.IsRepoFile ? (string.IsNullOrWhiteSpace(row.Url) ? null : row.Url.Trim()) : null,
                    LocalPath = row.IsLocalFile ? (string.IsNullOrWhiteSpace(row.LocalPath) ? null : row.LocalPath.Trim()) : null,
                    ModFolderId = row.Record.ModFolderId,
                    Enabled = row.Enabled,
                    Bundled = row.Record.Bundled
                }));
                _replacementSourceManager.CommitRegistryChanges();
            }

            if (_noVersionWarningSourceManager != null)
            {
                _noVersionWarningSourceManager.Registry.ReplaceAll(NoVersionWarningSources.Select(row => new ModRuleSourceRecord
                {
                    Id = row.Record.Id,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? row.Record.Id : row.Name.Trim(),
                    Type = row.Record.Type,
                    Url = row.IsRepoFile ? (string.IsNullOrWhiteSpace(row.Url) ? null : row.Url.Trim()) : null,
                    LocalPath = row.IsLocalFile ? (string.IsNullOrWhiteSpace(row.LocalPath) ? null : row.LocalPath.Trim()) : null,
                    ModFolderId = row.Record.ModFolderId,
                    Enabled = row.Enabled,
                    Bundled = row.Record.Bundled
                }));
                _noVersionWarningSourceManager.CommitRegistryChanges();
            }

            _configService.SaveConfig();

            // Refresh path service to pick up the new mods_folder value
            _pathService.RefreshPaths();

            CloseDialog(true);
        }
    }
}