#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Extensions;
using RimSharp.Features.LogViewer.Services;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.ModManagement;
using RimSharp.Services.Workshop;

namespace RimSharp.Features.Troubleshooting.ViewModels
{
    /// <summary>
    /// Wizard-state view model for the Troubleshooting dialog.
    /// States:
    /// - SymptomList: Initial list of symptoms (no scan)
    /// - CausesPage: Symptom selected, shows possible causes + "Check this problem" button
    /// - ChecksRunning: Checks executing, light progress (spinner + current check name)
    /// - SolutionsPage: Findings-driven solutions with checkboxes + fallback related actions
    /// - ActionRunning: Fix actions executing sequentially (non-blocking)
    /// - ActionResults: Per-action outcome list with Done affordance
    /// </summary>
    public sealed class TroubleshootingViewModel : DialogViewModelBase<bool>
    {
        private readonly IDialogService _dialogService;
        private readonly IGameRecoveryService _gameRecovery;
        private readonly IModConfigurationService _modConfig;
        private readonly ISteamSubscriptionService _steam;
        private readonly ITroubleshootingScanService _scan;
        private readonly Shared.Models.GameInstance _gameInstance;

        private readonly ObservableCollection<ProblemCardViewModel> _findings = new();
        private readonly ObservableCollection<ActionSectionViewModel> _sections = new();
        private readonly ObservableCollection<InlineToastViewModel> _toasts = new();
        private readonly ObservableCollection<TroubleshootingSymptomDescriptor> _visibleSymptoms = new();
        private readonly ObservableCollection<SolutionRowViewModel> _solutionRows = new();
        private readonly ObservableCollection<ActionResultViewModel> _actionResults = new();

        private readonly Dictionary<string, ActionRowViewModel> _rowsById = new();

        private string _scanCheck = string.Empty;
        private bool _isScanning;
        private bool _isActionRunning;
        private string _actionRunningTitle = string.Empty;
        private string _checksSummary = string.Empty;
        private string _noFindingsTitle = string.Empty;
        private string _noFindingsNote = string.Empty;
        private bool _runHadFindings;
        private TroubleshootingSymptomDescriptor? _currentSymptom;
        private TroubleshootingState _state = TroubleshootingState.SymptomList;
        private CancellationTokenSource? _checksCts;
        private CancellationTokenSource? _actionsCts;

        public TroubleshootingViewModel(
            IDialogService dialogService,
            IGameRecoveryService gameRecovery,
            IModConfigurationService modConfig,
            ISteamSubscriptionService steam,
            ITroubleshootingScanService scan,
            Shared.Models.GameInstance gameInstance)
            : base("Troubleshooting")
        {
            _dialogService = dialogService;
            _gameRecovery = gameRecovery;
            _modConfig = modConfig;
            _steam = steam;
            _scan = scan;
            _gameInstance = gameInstance;

            CloseCommand = ReactiveCommand.Create(() => CloseDialog(true));

            SelectSymptomCommand = CreateAsyncCommand<TroubleshootingSymptomDescriptor?>(
                ExecuteSelectSymptomAsync,
                symptom => _state == TroubleshootingState.SymptomList && !IsActionRunning,
                nameof(IsActionRunning), nameof(State));

            RunChecksCommand = CreateAsyncCommand(
                ExecuteRunChecksAsync,
                () => _state == TroubleshootingState.CausesPage && !IsActionRunning,
                nameof(IsActionRunning), nameof(State));

            PickDifferentProblemCommand = CreateAsyncCommand(
                ExecutePickDifferentProblemAsync,
                () => (_state == TroubleshootingState.CausesPage || _state == TroubleshootingState.SolutionsPage) && !IsActionRunning && !IsScanning,
                nameof(IsActionRunning), nameof(IsScanning), nameof(State));

            ProceedWithFixesCommand = CreateAsyncCommand(
                ExecuteProceedWithFixesAsync,
                () => _state == TroubleshootingState.SolutionsPage && _solutionRows.Any(r => r.IsSelected) && !IsActionRunning,
                nameof(IsActionRunning), nameof(State), nameof(HasSelectedSolutions));

            DismissActionResultsCommand = CreateAsyncCommand(
                ExecuteDismissActionResultsAsync,
                () => _state == TroubleshootingState.ActionResults,
                nameof(IsActionRunning), nameof(State));

            RunActionCommand = CreateAsyncCommand<string?>(
                ExecuteRunActionAsync,
                id => !IsActionRunning && id != null,
                nameof(IsActionRunning));

            DismissToastCommand = CreateAsyncCommand<InlineToastViewModel>(
                ExecuteDismissToastAsync,
                observedProperties: Array.Empty<string>());
        }

        private async Task ExecuteSelectSymptomAsync(TroubleshootingSymptomDescriptor? symptom)
        {
            if (symptom == null) return;
            await OpenCausesPageAsync(symptom);
        }

        private async Task ExecuteRunChecksAsync()
        {
            if (_currentSymptom == null) return;
            await RunSymptomChecksAsync(_currentSymptom);
        }

        private async Task ExecutePickDifferentProblemAsync()
        {
            // Cancel any in-progress checks
            _checksCts?.Cancel();
            _checksCts?.Dispose();
            _checksCts = null;

            _currentSymptom = null;
            _findings.Clear();
            ClearSolutionRows();
            _state = TroubleshootingState.SymptomList;
            IsScanning = false;
            ScanCheck = string.Empty;

            RaiseAllStateProperties();
            await Task.CompletedTask;
        }

        private async Task ExecuteProceedWithFixesAsync()
        {
            if (_currentSymptom == null) return;

            var selectedRows = _solutionRows.Where(r => r.IsSelected).ToList();
            if (selectedRows.Count == 0) return;

            _actionsCts = new CancellationTokenSource();
            var ct = _actionsCts.Token;

            _state = TroubleshootingState.ActionRunning;
            IsActionRunning = true;
            RaiseAllStateProperties();

            // Clear previous results
            await ThreadHelper.RunOnUIThreadAsync(() => _actionResults.Clear());

            try
            {
                foreach (var row in selectedRows)
                {
                    ct.ThrowIfCancellationRequested();

                    await ThreadHelper.RunOnUIThreadAsync(() =>
                    {
                        ActionRunningTitle = row.Title;
                    });

                    // No confirmation modal: checking the box and clicking
                    // "Proceed with fixes" IS the authorization (user decision).

                    var result = await ExecuteFixAsync(row.ActionId);

                    await ThreadHelper.RunOnUIThreadAsync(() =>
                    {
                        _actionResults.Add(new ActionResultViewModel
                        {
                            ActionTitle = row.Title,
                            Success = result.Success,
                            Message = result.Success ? result.Message : $"Failed: {result.Message}"
                        });
                    });

                    // Refresh findings after destructive actions
                    if (row.IsDestructive)
                    {
                        await RunSymptomChecksAsync(_currentSymptom!);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    _actionResults.Add(new ActionResultViewModel
                    {
                        ActionTitle = ActionRunningTitle,
                        Success = false,
                        Message = $"Failed: {ex.Message}"
                    });
                });
            }
            finally
            {
                _actionsCts?.Dispose();
                _actionsCts = null;

                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    IsActionRunning = false;
                    ActionRunningTitle = string.Empty;
                    _state = TroubleshootingState.ActionResults;
                    RaiseAllStateProperties();
                });
            }
        }

        private async Task ExecuteDismissActionResultsAsync()
        {
            // User is done with this problem entirely — offer the next problem,
            // not the same solutions page. Full reset back to the symptom list.
            _checksCts?.Cancel();
            _checksCts?.Dispose();
            _checksCts = null;

            await ThreadHelper.RunOnUIThreadAsync(() =>
            {
                _actionResults.Clear();
                _currentSymptom = null;
                _runHadFindings = false;
                _checksSummary = string.Empty;
                _noFindingsTitle = string.Empty;
                _noFindingsNote = string.Empty;
                ClearSolutionRows();
                _findings.Clear();
                _state = TroubleshootingState.SymptomList;
                RaiseAllStateProperties();
            });
            await Task.CompletedTask;
        }

        private async Task ExecuteRunActionAsync(string? actionId)
        {
            if (actionId == null) return;
            await RunActionAsync(actionId);
        }

        private async Task ExecuteDismissToastAsync(InlineToastViewModel toast)
        {
            _toasts.Remove(toast);
            await Task.CompletedTask;
        }

        public ObservableCollection<ProblemCardViewModel> Findings => _findings;
        public ObservableCollection<ActionSectionViewModel> Sections => _sections;
        public ObservableCollection<InlineToastViewModel> Toasts => _toasts;
        public ObservableCollection<TroubleshootingSymptomDescriptor> VisibleSymptoms => _visibleSymptoms;
        public ObservableCollection<SolutionRowViewModel> SolutionRows => _solutionRows;
        public ObservableCollection<ActionResultViewModel> ActionResults => _actionResults;

        public string ScanCheck
        {
            get => _scanCheck;
            private set => this.RaiseAndSetIfChanged(ref _scanCheck, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            private set => this.RaiseAndSetIfChanged(ref _isScanning, value);
        }

        public bool IsActionRunning
        {
            get => _isActionRunning;
            private set => this.RaiseAndSetIfChanged(ref _isActionRunning, value);
        }

        public string ActionRunningTitle
        {
            get => _actionRunningTitle;
            private set => this.RaiseAndSetIfChanged(ref _actionRunningTitle, value);
        }

        /// <summary>The currently selected symptom (null when showing the symptom list).</summary>
        public TroubleshootingSymptomDescriptor? CurrentSymptom => _currentSymptom;

        public string CurrentSymptomTitle => _currentSymptom?.Title ?? string.Empty;
        public string CurrentSymptomDescription => _currentSymptom?.Description ?? string.Empty;
        public IReadOnlyList<string> CurrentSymptomCauses => _currentSymptom?.Causes ?? Array.Empty<string>();

        public TroubleshootingState State => _state;

        public bool ShowSymptomList => _state == TroubleshootingState.SymptomList;
        public bool ShowCausesPage => _state == TroubleshootingState.CausesPage;
        public bool ShowChecksRunning => _state == TroubleshootingState.ChecksRunning;
        public bool ShowSolutionsPage => _state == TroubleshootingState.SolutionsPage;
        public bool ShowActionRunning => _state == TroubleshootingState.ActionRunning;
        public bool ShowActionResults => _state == TroubleshootingState.ActionResults;

        /// <summary>True when the last check run produced fixable findings (drives the zero-note panel).</summary>
        public bool HasFindings => _runHadFindings || _findings.Count > 0;
        public bool HasSelectedSolutions => _solutionRows.Any(r => r.IsSelected);

        /// <summary>
        /// One-line honest record of what the last check run actually did
        /// (or that this symptom has no automatic checks). Empty before the first run.
        /// </summary>
        public string ChecksSummary => _checksSummary;
        public bool HasChecksSummary => !string.IsNullOrEmpty(_checksSummary);
        public string NoFindingsTitle => _noFindingsTitle;
        public string NoFindingsNote => _noFindingsNote;

        public new ICommand CloseCommand { get; }
        public ICommand SelectSymptomCommand { get; }
        public ICommand RunChecksCommand { get; }
        public ICommand PickDifferentProblemCommand { get; }
        public ICommand ProceedWithFixesCommand { get; }
        public ICommand DismissActionResultsCommand { get; }
        public ICommand RunActionCommand { get; }
        public ICommand DismissToastCommand { get; }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            BuildCatalog();
            BuildSymptomList();
        }

        private void BuildCatalog()
        {
            _sections.Clear();
            _rowsById.Clear();

            var isSteamInstallation = _gameInstance.IsSteamInstallation;

            var gameFiles = new ActionSectionViewModel("Game Files Recovery", "Recover game files, configs, caches, and logs.");
            AddRow(gameFiles, "reset_game_files", "Reset Game Files",
                "Delete game files (except Mods) and trigger Steam reinstall.", "fa-rotate-right", true, true, true);
            AddRow(gameFiles, "reset_steam_workshop_mods", "Reset Steam Workshop Mods",
                "Delete workshop content and trigger Steam redownload.", "fa-trash", true, true, true);
            AddRow(gameFiles, "reset_mod_configurations", "Reset Mod Configurations",
                "Delete config files except ModsConfig.xml and Prefs.xml.", "fa-eraser", false, true, true);
            AddRow(gameFiles, "reset_game_configurations", "Reset Game Configurations",
                "Delete ModsConfig.xml, Prefs.xml, KeyPrefs.xml (regenerate on launch).", "fa-eraser", false, true, true);
            AddRow(gameFiles, "clean_log_files", "Clean Huge Log Files",
                "Truncate/delete Player.log when larger than 50 MB.", "fa-file-lines", false, false, false);
            AddRow(gameFiles, "reset_display_resolution", "Reset Display & Resolution",
                "Reset display nodes in Prefs.xml to safe defaults.", "fa-display", false, true, false);
            AddRow(gameFiles, "clear_mod_runtime_caches", "Clear Mod Runtime Caches",
                "Delete PrepatcherCache, temp Prepatcher, HugsLib state.", "fa-broom", false, false, false);
            AddRow(gameFiles, "check_file_permissions", "Check File Permissions & Cloud Locks",
                "Detect OneDrive reparse/offline/read-only attributes.", "fa-shield-halved", false, false, false);
            AddRow(gameFiles, "scan_missing_assemblies", "Scan for Missing Critical Mod Assemblies",
                "Detect zero-byte/inaccessible mod DLLs.", "fa-microchip", false, false, false);
            AddRow(gameFiles, "verify_mod_settings_xml", "Verify Mod Settings XML Integrity",
                "Scan Mod_*.xml for 0-byte/NUL/malformed files.", "fa-file-circle-exclamation", false, true, true);
            AddRow(gameFiles, "check_path_compatibility", "Path Compatibility Diagnostic",
                "Warn when paths contain non-ASCII characters.", "fa-route", false, false, false);

            var modConfig = new ActionSectionViewModel("Mod Configuration", "Manage your mod list and configuration.");
            AddRow(modConfig, "clear_all_mods", "Clear All Mods",
                "Delete Mods folder contents; reset ModsConfig.xml to vanilla.", "fa-trash", false, true, true);
            AddRow(modConfig, "restore_mods_config_from_backup", "Restore ModsConfig from Backup",
                "Restore ModsConfig.xml from a rolling backup.", "fa-rotate-left", false, false, false);
            AddRow(modConfig, "sanitize_core_dlc_order", "Sanitize Core & DLC Load Order",
                "Enforce Core/DLC hierarchy in ModsConfig.xml.", "fa-sort", false, true, false);
            AddRow(modConfig, "analyze_saves_quota", "Analyze Save Folder & Cloud Quota",
                "Report saves folder size vs Steam Cloud quota.", "fa-folder-tree", false, false, false);

            var steamSection = new ActionSectionViewModel("Steam Utilities", "Steam client maintenance actions.", isSteamSection: true);
            AddRow(steamSection, "clear_download_cache", "Clear Download Cache",
                "Delete steamapps/downloading and workshop staging dirs.", "fa-download", true, false, false);
            AddRow(steamSection, "verify_game_files", "Verify Game Files",
                "Launch Steam's verify integrity of game files for RimWorld.", "fa-circle-check", true, false, false);
            AddRow(steamSection, "repair_library", "Repair Steam Library",
                "Validate all apps via steam://validate from libraryfolders.vdf.", "fa-hard-drive", true, false, false);
            AddRow(steamSection, "clean_orphaned_workshop_mods", "Clean Orphaned Workshop Mods",
                "Remove ghost mods from appworkshop_294100.acf.", "fa-trash", true, true, true);
            AddRow(steamSection, "kill_zombie_processes", "Kill Zombie RimWorld Processes",
                "Terminate stuck RimWorld/UnityCrashHandler64 processes.", "fa-skull-crossbones", false, true, false);
            AddRow(steamSection, "purge_workshop_temp", "Purge Workshop Temp/Staging",
                "Delete workshop downloads and temp/294100 staging dirs.", "fa-broom", true, false, false);

            _sections.Add(gameFiles);
            _sections.Add(modConfig);
            _sections.Add(steamSection);

            ApplySteamGating(isSteamInstallation);
        }

        private void BuildSymptomList()
        {
            _visibleSymptoms.Clear();
            var isSteamInstallation = _gameInstance.IsSteamInstallation;
            foreach (var symptom in TroubleshootingSymptoms.GetVisible(isSteamInstallation))
            {
                _visibleSymptoms.Add(symptom);
            }
        }

        private bool _isSteamInstallation;

        private void AddRow(ActionSectionViewModel section, string id, string title, string description,
            string icon, bool steamRequired, bool destructive, bool requiresConfirmation)
        {
            var isVisible = !steamRequired || _isSteamInstallation;
            var row = new ActionRowViewModel(id, title, description, icon, section.Title,
                steamRequired, destructive, requiresConfirmation, isVisible);
            section.Actions.Add(row);
            _rowsById[id] = row;
        }

        private void ApplySteamGating(bool isSteamInstallation)
        {
            _isSteamInstallation = isSteamInstallation;
            foreach (var section in _sections)
            {
                if (section.IsSteamSection)
                {
                    section.IsVisible = isSteamInstallation;
                }
            }
            foreach (var row in _rowsById.Values)
            {
                if (row.IsSteamRequired)
                {
                    row.IsVisible = isSteamInstallation;
                }
            }
        }

        private async Task OpenCausesPageAsync(TroubleshootingSymptomDescriptor symptom)
        {
            _currentSymptom = symptom;
            _findings.Clear();
            ClearSolutionRows();

            _state = TroubleshootingState.CausesPage;
            RaiseAllStateProperties();
            await Task.CompletedTask;
        }

        private async Task RunSymptomChecksAsync(TroubleshootingSymptomDescriptor symptom)
        {
            _checksCts = new CancellationTokenSource();
            var ct = _checksCts.Token;

            _state = TroubleshootingState.ChecksRunning;
            IsScanning = true;
            ScanCheck = "Starting checks…";
            RaiseAllStateProperties();

            try
            {
                var checkIds = symptom.CheckIds;
                if (checkIds.Count == 0)
                {
                    // No checks for this symptom — go straight to solutions, with an honest
                    // note that nothing was checked (never claim checks "passed").
                    await ThreadHelper.RunOnUIThreadAsync(() =>
                    {
                        IsScanning = false;
                        ScanCheck = string.Empty;
                        _checksSummary = "No checks were run for this problem — it has no automatic checks yet.";
                        _noFindingsTitle = "No automatic checks for this problem yet";
                        _noFindingsNote = "This problem has no automatic checks. The actions below address it directly — select what you want done and proceed.";
                        _runHadFindings = false;
                        BuildSolutionRows(symptom, Array.Empty<ScanFinding>());
                        _state = TroubleshootingState.SolutionsPage;
                        RaiseAllStateProperties();
                    });
                    return;
                }

                var sw = Stopwatch.StartNew();
                var checksRun = 0;

                // Run checks on background thread
                await Task.Run(async () =>
                {
                    var allFindings = new List<ScanFinding>();

                    foreach (var checkId in checkIds)
                    {
                        ct.ThrowIfCancellationRequested();

                        // Update current check name on UI thread
                        await ThreadHelper.RunOnUIThreadAsync(() =>
                        {
                            ScanCheck = $"Checking {GetCheckDisplayName(checkId)}…";
                        });

                        // Run the check
                        var findings = await _scan.RunChecksAsync(new[] { checkId }, ct);

                        // Collect findings on UI thread
                        await ThreadHelper.RunOnUIThreadAsync(() =>
                        {
                            allFindings.AddRange(findings);
                        });
                        checksRun++;
                    }

                    // Keep the running state perceivable when checks are fast.
                    // Delay happens HERE on the background thread — never inside a
                    // UI-thread marshaled lambda.
                    var workMs = sw.ElapsedMilliseconds;
                    if (workMs < MinChecksVisibleMs)
                        await TestChecksDelayOverride((int)(MinChecksVisibleMs - workMs), ct);

                    // Build solutions and transition on UI thread
                    await ThreadHelper.RunOnUIThreadAsync(() =>
                    {
                        IsScanning = false;
                        ScanCheck = string.Empty;
                        _checksSummary = $"Checks completed in {workMs} ms — {checksRun} check{(checksRun == 1 ? string.Empty : "s")} run.";
                        _noFindingsTitle = "No specific problems were found for this problem.";
                        _noFindingsNote = $"Nothing wrong was found: {checksRun} check{(checksRun == 1 ? string.Empty : "s")} ran and passed ({workMs} ms). You can still run the related actions below if needed.";
                        BuildSolutionRows(symptom, allFindings);
                        _runHadFindings = _solutionRows.Any(r => r.IsFindingDriven);
                        _state = TroubleshootingState.SolutionsPage;
                        RaiseAllStateProperties();
                    });
                }, ct);
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    IsScanning = false;
                    ScanCheck = string.Empty;
                    _state = TroubleshootingState.CausesPage;
                    RaiseAllStateProperties();
                });
            }
            catch (Exception ex)
            {
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    IsScanning = false;
                    ScanCheck = string.Empty;
                    AddToast("Scan Error", $"Failed to run checks: {ex.Message}", ToastType.Error);
                    _state = TroubleshootingState.CausesPage;
                    RaiseAllStateProperties();
                });
            }
            finally
            {
                _checksCts?.Dispose();
                _checksCts = null;
            }
        }

        private void RaiseAllStateProperties()
        {
            this.RaisePropertyChanged(nameof(ShowSymptomList));
            this.RaisePropertyChanged(nameof(ShowCausesPage));
            this.RaisePropertyChanged(nameof(ShowChecksRunning));
            this.RaisePropertyChanged(nameof(ShowSolutionsPage));
            this.RaisePropertyChanged(nameof(ShowActionRunning));
            this.RaisePropertyChanged(nameof(ShowActionResults));
            this.RaisePropertyChanged(nameof(CurrentSymptomTitle));
            this.RaisePropertyChanged(nameof(CurrentSymptomDescription));
            this.RaisePropertyChanged(nameof(CurrentSymptomCauses));
            this.RaisePropertyChanged(nameof(State));
            this.RaisePropertyChanged(nameof(HasFindings));
            this.RaisePropertyChanged(nameof(HasSelectedSolutions));
            this.RaisePropertyChanged(nameof(ChecksSummary));
            this.RaisePropertyChanged(nameof(HasChecksSummary));
            this.RaisePropertyChanged(nameof(NoFindingsTitle));
            this.RaisePropertyChanged(nameof(NoFindingsNote));
        }

        /// <summary>
        /// Minimum time the ChecksRunning state stays visible when checks actually
        /// run, so fast checks never blink past unread. Never applies when a
        /// symptom has zero checks (no fake progress is shown in that case).
        /// </summary>
        private const int MinChecksVisibleMs = 600;

        /// <summary>Test seam for the minimum-visible delay (defaults to Task.Delay).</summary>
        internal Func<int, CancellationToken, Task> TestChecksDelayOverride { get; set; } = Task.Delay;

        /// <summary>
        /// Every fix action runs on a background thread so heavy directory/file
        /// work can never block the UI. (An earlier version exempted export/import
        /// because they showed file pickers; those actions are now removed from
        /// this catalog — the maintenance menu already has them.)
        /// </summary>
        private void OnSolutionRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SolutionRowViewModel.IsSelected))
                this.RaisePropertyChanged(nameof(HasSelectedSolutions));
        }

        private void ClearSolutionRows()
        {
            foreach (var row in _solutionRows)
                row.PropertyChanged -= OnSolutionRowPropertyChanged;
            _solutionRows.Clear();
            this.RaisePropertyChanged(nameof(HasSelectedSolutions));
        }

        private static string GetCheckDisplayName(string checkId)
        {
            return checkId switch
            {
                "paths_resolve" => "game and config paths",
                "game_version" => "game version",
                "running_processes" => "running processes",
                "config_folder_attributes" => "config folder attributes",
                "log_signatures" => "log signatures",
                "mod_assemblies" => "mod assemblies",
                "log_file_sizes" => "log file sizes",
                "mod_settings_xml" => "mod settings XML",
                "path_compatibility" => "path compatibility",
                "workshop_mods" => "workshop mods",
                "orphaned_workshop_mods" => "orphaned workshop mods",
                "stuck_downloads" => "stuck downloads",
                "mods_config_integrity" => "ModsConfig.xml integrity",
                "saves_folder_size" => "saves folder size",
                _ => checkId
            };
        }

        private void BuildSolutionRows(TroubleshootingSymptomDescriptor symptom, IReadOnlyList<ScanFinding> findings)
        {
            ClearSolutionRows();

            // First: findings-driven solutions (from checks that found issues)
            var findingDrivenActions = new Dictionary<string, (ScanFinding Finding, ActionRowViewModel Row)>();

            foreach (var f in findings)
            {
                // Skip informational/prerequisite checks
                if (f.CheckId == "steam_installation" || f.CheckId == "game_version" || f.CheckId == "paths_resolve")
                    continue;

                var (hasFix, actionId, actionTitle, whatItMeans, suggestion) = MapFinding(f);
                if (hasFix && actionId != null && _rowsById.TryGetValue(actionId, out var row))
                {
                    findingDrivenActions[actionId] = (f, row);
                }
            }

            // Add findings-driven rows first
            foreach (var kvp in findingDrivenActions)
            {
                var finding = kvp.Value.Finding;
                var row = kvp.Value.Row;

                _solutionRows.Add(new SolutionRowViewModel
                {
                    ActionId = row.Id,
                    Title = row.Title,
                    Description = row.Description,
                    IsDestructive = row.IsDestructive,
                    IsFindingDriven = true,
                    FindingDetail = finding.Detail,
                    FindingSeverity = finding.Severity.ToString(),
                    IsSelected = false
                });
                _solutionRows[^1].PropertyChanged += OnSolutionRowPropertyChanged;
            }

            // Then: fallback related actions (from symptom descriptor)
            foreach (var actionId in symptom.RelatedActionIds)
            {
                if (findingDrivenActions.ContainsKey(actionId)) continue; // Already added

                if (_rowsById.TryGetValue(actionId, out var row) && row.IsVisible)
                {
                    _solutionRows.Add(new SolutionRowViewModel
                    {
                        ActionId = row.Id,
                        Title = row.Title,
                        Description = row.Description,
                        IsDestructive = row.IsDestructive,
                        IsFindingDriven = false,
                        FindingDetail = null,
                        FindingSeverity = null,
                        IsSelected = false
                    });
                    _solutionRows[^1].PropertyChanged += OnSolutionRowPropertyChanged;
                }
            }
        }

        private (bool hasFix, string? actionId, string? actionTitle, string whatItMeans, string? suggestion) MapFinding(ScanFinding f)
        {
            var suggestion = f.Metadata != null && f.Metadata.TryGetValue("suggestion", out var s) ? s?.ToString() : null;

            return f.SuggestedActionId switch
            {
                "clean_log_files" => (true, "clean_log_files", "Clean Huge Log Files", f.Detail, null),
                "kill_zombie_processes" => (true, "kill_zombie_processes", "Kill Zombie RimWorld Processes", f.Detail, null),
                "check_file_permissions" => (true, "check_file_permissions", "Check File Permissions & Cloud Locks", f.Detail, null),
                "scan_missing_assemblies" => (true, "scan_missing_assemblies", "Scan for Missing Critical Mod Assemblies", f.Detail, null),
                "verify_mod_settings_xml" => (true, "verify_mod_settings_xml", "Verify Mod Settings XML Integrity", f.Detail, null),
                "check_path_compatibility" => (true, "check_path_compatibility", "Path Compatibility Diagnostic", f.Detail, null),
                "reset_steam_workshop_mods" => (true, "reset_steam_workshop_mods", "Reset Steam Workshop Mods", f.Detail, null),
                "clean_orphaned_workshop_mods" => (true, "clean_orphaned_workshop_mods", "Clean Orphaned Workshop Mods", f.Detail, null),
                "clear_download_cache" => (true, "clear_download_cache", "Clear Download Cache", f.Detail, null),
                "purge_workshop_temp" => (true, "purge_workshop_temp", "Purge Workshop Temp/Staging", f.Detail, null),
                "restore_mods_config_from_backup" => (true, "restore_mods_config_from_backup", "Restore ModsConfig from Backup", f.Detail, null),
                "analyze_saves_quota" => (true, "analyze_saves_quota", "Analyze Save Folder & Cloud Quota", f.Detail, null),
                "sanitize_core_dlc_order" => (true, "sanitize_core_dlc_order", "Sanitize Core & DLC Load Order", f.Detail, null),
                null => (false, null, null, f.Detail, suggestion),
                _ => (false, null, null, f.Detail, suggestion)
            };
        }

        public async Task RunActionAsync(string actionId)
        {
            if (!_rowsById.TryGetValue(actionId, out var row)) return;
            if (row.IsRunning) return;

            // No confirmation modal: the explicit click on the fix itself is
            // the authorization (user decision — "proceed button is enough").
            // For batch runs, checkbox + "Proceed with fixes" likewise.

            row.SetRunning(true);
            IsActionRunning = true;
            ActionRunningTitle = row.Title;
            _state = TroubleshootingState.ActionRunning;
            RaiseAllStateProperties();

            try
            {
                var result = await ExecuteFixAsync(actionId);

                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    row.SetRunning(false);
                    IsActionRunning = false;
                    ActionRunningTitle = string.Empty;

                    if (result.Success)
                    {
                        AddToast(row.Title, result.Message, ToastType.Success);
                    }
                    else
                    {
                        AddToast(row.Title, $"Failed: {result.Message}", ToastType.Error);
                    }

                    _state = TroubleshootingState.SolutionsPage;
                    RaiseAllStateProperties();
                });

                // Refresh the current symptom's checks after destructive actions so findings update
                if (row.IsDestructive && _currentSymptom != null)
                {
                    await RunSymptomChecksAsync(_currentSymptom);
                }
            }
            catch (Exception ex)
            {
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    row.SetRunning(false);
                    IsActionRunning = false;
                    ActionRunningTitle = string.Empty;
                    AddToast(row.Title, $"Failed: {ex.Message}", ToastType.Error);
                    _state = TroubleshootingState.SolutionsPage;
                    RaiseAllStateProperties();
                });
            }
        }

        /// <summary>
        /// Runs the fix on a background thread. Every action here is catalog-local:
        /// heavy directory/file work (e.g. the assembly scan froze the app when it
        /// ran inline) must never execute on the UI thread.
        /// </summary>
        private Task<GameRecoveryResult> ExecuteFixAsync(string actionId) =>
            Task.Run(() => ExecuteActionAsync(actionId));

        private async Task<GameRecoveryResult> ExecuteActionAsync(string actionId)
        {
            return actionId switch
            {
                "reset_game_files" => await _gameRecovery.ResetGameFilesAsync(),
                "reset_steam_workshop_mods" => await _gameRecovery.ResetSteamWorkshopModsAsync(),
                "reset_mod_configurations" => await _gameRecovery.ResetModConfigurationsAsync(),
                "reset_game_configurations" => await _gameRecovery.ResetGameConfigurationsAsync(),
                "clean_log_files" => await _gameRecovery.CleanLogFilesAsync(),
                "reset_display_resolution" => await _gameRecovery.ResetDisplayResolutionAsync(),
                "clear_mod_runtime_caches" => await _gameRecovery.ClearModRuntimeCachesAsync(),
                "check_file_permissions" => await _gameRecovery.CheckFilePermissionsAsync(),
                "scan_missing_assemblies" => await _gameRecovery.ScanMissingAssembliesAsync(),
                "verify_mod_settings_xml" => await _gameRecovery.VerifyModSettingsXmlAsync(),
                "check_path_compatibility" => await _gameRecovery.CheckPathCompatibilityAsync(),
                "clear_all_mods" => await WrapModConfig(() => _modConfig.ClearAllModsAsync()),
                "restore_mods_config_from_backup" => await WrapModConfig(() => _modConfig.RestoreModsConfigFromBackupAsync("")),
                "sanitize_core_dlc_order" => await WrapModConfig(() => _modConfig.SanitizeCoreDlcOrderAsync()),
                "analyze_saves_quota" => await WrapModConfig(() => _modConfig.AnalyzeSavesQuotaAsync()),
                "clear_download_cache" => await _steam.ClearDownloadCacheAsync(),
                "verify_game_files" => await WrapSteamBool(() => _steam.VerifyGameFilesAsync()),
                "repair_library" => await _steam.RepairLibraryAsync(),
                "clean_orphaned_workshop_mods" => await _steam.CleanOrphanedWorkshopModsAsync(),
                "kill_zombie_processes" => await _steam.KillZombieProcessesAsync(),
                "purge_workshop_temp" => await _steam.PurgeWorkshopTempAsync(),
                _ => GameRecoveryResult.Fail($"Unknown action: {actionId}")
            };
        }

        private static async Task<GameRecoveryResult> WrapModConfig(Func<Task<ModConfigurationResult>> fn)
        {
            var r = await fn();
            return r.Success
                ? GameRecoveryResult.Ok(r.Message, r.Count, r.Details)
                : GameRecoveryResult.Fail(r.Message, r.Details);
        }

        private static async Task<GameRecoveryResult> WrapSteamBool(Func<Task<bool>> fn)
        {
            var ok = await fn();
            return ok
                ? GameRecoveryResult.Ok("Action completed.")
                : GameRecoveryResult.Fail("Action did not complete.");
        }

        private void AddToast(string title, string message, ToastType type)
        {
            _toasts.Add(new InlineToastViewModel(title, message, type));
        }

        public void DismissToast(InlineToastViewModel toast)
        {
            _toasts.Remove(toast);
        }
    }

    /// <summary>
    /// Troubleshooting dialog state machine.
    /// </summary>
    public enum TroubleshootingState
    {
        SymptomList,
        CausesPage,
        ChecksRunning,
        SolutionsPage,
        ActionRunning,
        ActionResults
    }

    /// <summary>
    /// A solution row in the SolutionsPage with checkbox selection.
    /// </summary>
    public sealed class SolutionRowViewModel : ReactiveObject
    {
        public string ActionId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsDestructive { get; init; }
        public bool IsFindingDriven { get; init; }
        public string? FindingDetail { get; init; }
        public string? FindingSeverity { get; init; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public string ExplanationText
        {
            get
            {
                if (IsFindingDriven && !string.IsNullOrEmpty(FindingDetail))
                {
                    return $"{Description}\n\nAddresses: {FindingDetail}";
                }
                return Description;
            }
        }

        public string SeverityBadge => FindingSeverity switch
        {
            "Error" => "Error",
            "Warning" => "Warning",
            _ => ""
        };
    }

    /// <summary>
    /// Result of a single action execution in the batch.
    /// </summary>
    public sealed class ActionResultViewModel : ReactiveObject
    {
        public string ActionTitle { get; init; } = string.Empty;
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;

        public string StatusIcon => Success ? "✓" : "✕";
        public string StatusColor => Success ? "BrushTextSuccess" : "BrushTextError";
    }
}