#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Features.LogViewer.Services;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.LogViewer.ViewModels
{
    /// <summary>
    /// Drives the standalone Player.log viewer window: locates the log, reads it
    /// through <see cref="PlayerLogReader"/>, keeps content in a
    /// <see cref="LogContentStore"/> and exposes classified messages for display.
    /// Also owns the source switcher (Player.log / Player-prev.log / a file the
    /// user opened by hand), the load-from-file and export commands, and the
    /// repeated-line collapse toggle.
    /// </summary>
    public class LogViewerViewModel : DialogViewModelBase<bool>
    {
        private readonly IPathService? _pathService;
        private readonly IFileDialogService? _fileDialogService;
        private readonly PlayerLogReader _reader;
        private readonly LogContentStore _store = new();
        private readonly List<LogEntryViewModel> _allEntries = new();

        private string? _currentPath;
        private HugsLogEnvelope? _envelope;
        private bool _isLoading;
        private bool _collapseRepeats = true;
        private bool _syncingSource;

        public LogViewerViewModel(
            IPathService? pathService = null,
            PlayerLogReader? reader = null,
            IFileDialogService? fileDialogService = null)
            : base("Log Viewer")
        {
            _pathService = pathService;
            _fileDialogService = fileDialogService;
            _reader = reader ?? new PlayerLogReader();
            _selectedFilterOption = FilterOptions[0];
            LoadDefaultCommand = CreateAsyncCommand(LoadDefaultAsync, () => !IsLoading, nameof(IsLoading));
            RefreshCommand = CreateAsyncCommand(RefreshAsync, () => !IsLoading, nameof(IsLoading));
            LoadFileCommand = CreateAsyncCommand(LoadFromFileAsync, () => !IsLoading, nameof(IsLoading));
            ExportCommand = CreateAsyncCommand(
                ExportAsync,
                () => !IsLoading && LoadedCount > 0,
                nameof(IsLoading),
                nameof(LoadedCount));
            SourceOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultipleSources));
        }

        /// <summary>Troubleshooter, summary and startup-analysis state for the report tabs.</summary>
        public LogDiagnosticsViewModel Diagnostics { get; } = new();

        /// <summary>Filter modes offered by the combo box.</summary>
        public IReadOnlyList<LogFilterOption> FilterOptions { get; } = new[]
        {
            new LogFilterOption("All Entries", LogFilterMode.AllEntries),
            new LogFilterOption("Infos Only", LogFilterMode.InfosOnly),
            new LogFilterOption("Keybinds Only", LogFilterMode.KeybindsOnly),
            new LogFilterOption("Mod Issues", LogFilterMode.ModIssues),
            new LogFilterOption("Warnings Only", LogFilterMode.WarningsOnly),
            new LogFilterOption("Errors Only", LogFilterMode.ErrorsOnly),
            new LogFilterOption("Exceptions Only", LogFilterMode.ExceptionsOnly),
            new LogFilterOption("All Issues", LogFilterMode.AllIssues)
        };

        /// <summary>
        /// Log files the viewer can read: the located Player.log / Player-prev.log
        /// sources plus, once used, whatever file was opened by hand. Rebuilt on
        /// every load so it always reflects the files that exist right now.
        /// </summary>
        public ObservableCollection<LogSourceOption> SourceOptions { get; } = new();

        /// <summary>True once more than one source is readable (the switcher is only useful then).</summary>
        public bool HasMultipleSources => SourceOptions.Count > 1;

        private LogSourceOption? _selectedSourceOption;
        public LogSourceOption? SelectedSourceOption
        {
            get => _selectedSourceOption;
            set
            {
                if (!SetProperty(ref _selectedSourceOption, value)) return;

                // While the VM syncs the combo to what it just loaded, a selection
                // change must not reload and loop. Real user selections do switch.
                if (_syncingSource || value == null) return;
                SwitchTask = SwitchSourceAsync(value);
            }
        }

        /// <summary>
        /// The load started by the latest user-initiated source switch. Tests await
        /// this instead of sleeping; the view ignores it (the load updates state).
        /// </summary>
        public Task? SwitchTask { get; private set; }

        /// <summary>Whole messages bound to the virtualizing display list (after filtering).</summary>
        public ObservableCollection<LogEntryViewModel> Lines { get; } = new();

        /// <summary>Message count after collapse; drives Export's enabled state.</summary>
        public int LoadedCount => _allEntries.Count;

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Consecutive identical messages display as one entry annotated with the
        /// repeat count. Statistics count each collapsed run once, matching a
        /// storage-level collapse. Toggling re-groups the loaded content.
        /// </summary>
        public bool CollapseRepeats
        {
            get => _collapseRepeats;
            set
            {
                if (!SetProperty(ref _collapseRepeats, value)) return;
                RebuildAllLines();
                ApplyDisplay();
            }
        }

        private string _statusMessage = "Load the game's Player.log to start reading.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _pathText = "No log loaded";
        public string PathText
        {
            get => _pathText;
            private set => SetProperty(ref _pathText, value);
        }

        private string _sizeText = string.Empty;
        public string SizeText
        {
            get => _sizeText;
            private set => SetProperty(ref _sizeText, value);
        }

        private string _modifiedText = string.Empty;
        /// <summary>Last-write timestamp of the loaded file, for the file-info header.</summary>
        public string ModifiedText
        {
            get => _modifiedText;
            private set => SetProperty(ref _modifiedText, value);
        }

        private string _lineCountText = "0 lines";
        public string LineCountText
        {
            get => _lineCountText;
            set => SetProperty(ref _lineCountText, value);
        }

        /// <summary>
        /// True while no log content has been read at all — the view shows an
        /// empty-state hint inside the log area. A filter that hides every line
        /// of a loaded log does not count; the status message covers that case.
        /// </summary>
        public bool ShowLogPlaceholder => _store.LineCount == 0;

        private LogFilterOption _selectedFilterOption;
        public LogFilterOption SelectedFilterOption
        {
            get => _selectedFilterOption;
            set
            {
                if (SetProperty(ref _selectedFilterOption, value) && value != null)
                {
                    ApplyDisplay();
                }
            }
        }

        private string _lineFilter = string.Empty;
        public string LineFilter
        {
            get => _lineFilter;
            private set => SetProperty(ref _lineFilter, value);
        }

        private LogStats _stats = new();
        /// <summary>Category counters over the full loaded content (not the filtered view).</summary>
        public LogStats Stats
        {
            get => _stats;
            private set => SetProperty(ref _stats, value);
        }

        public ICommand LoadDefaultCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand LoadFileCommand { get; }
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Applies the line-text filter (called from the view, debounced).
        /// Composes with the selected filter mode.
        /// </summary>
        public void ApplyLineFilter(string? value)
        {
            var normalized = value ?? string.Empty;
            if (LineFilter == normalized) return;
            LineFilter = normalized;
            ApplyDisplay();
        }

        /// <summary>Locates Player.log beside the game's config folder and loads it.</summary>
        public async Task LoadDefaultAsync()
        {
            RebuildSourceOptions();

            var sources = PlayerLogLocator.Resolve(
                _pathService?.GetConfigPath(),
                _pathService?.GetMajorGameVersion());
            if (sources.Current == null)
            {
                PathText = "Player.log not found";
                StatusMessage = "Player.log was not found in the expected location for this game version.";
                return;
            }

            await LoadAsync(sources.Current);
        }

        /// <summary>Re-reads the current log from the top.</summary>
        public async Task RefreshAsync()
        {
            if (_currentPath == null)
            {
                await LoadDefaultAsync();
                return;
            }

            await LoadAsync(_currentPath);
        }

        /// <summary>Opens any log file through the file dialog and reads it.</summary>
        public async Task LoadFromFileAsync()
        {
            if (_fileDialogService == null)
            {
                StatusMessage = "Opening a file by hand needs the file dialog service.";
                return;
            }

            var (success, path) = await _fileDialogService.ShowOpenFileDialogAsync(
                "Open a RimWorld log file or shared export",
                "Log files and shared exports|*.log;*.txt|All files|*.*");
            if (!success || string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "Open cancelled.";
                return;
            }

            await LoadAsync(path);
        }

        /// <summary>Writes the currently displayed (filtered) lines to a file.</summary>
        public async Task ExportAsync()
        {
            if (Lines.Count == 0)
            {
                StatusMessage = "Nothing to export — no lines are displayed.";
                return;
            }

            if (_fileDialogService == null)
            {
                StatusMessage = "Export needs the file dialog service.";
                return;
            }

            var (success, path) = await _fileDialogService.ShowSaveFileDialogAsync(
                "Export the displayed lines",
                "Text files|*.txt|Log files|*.log|All files|*.*",
                defaultExtension: "txt",
                defaultFileName: "Player-log-export.txt");
            if (!success || string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "Export cancelled.";
                return;
            }

            try
            {
                var text = string.Join(Environment.NewLine, Lines.Select(l => l.DisplayText)) + Environment.NewLine;
                await File.WriteAllTextAsync(path, text);
                StatusMessage = $"Exported {Lines.Count:N0} lines to {path}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
        }

        /// <summary>Reads any log file (also the path for switching sources).</summary>
        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            IsLoading = true;
            try
            {
                Lines.Clear();
                _store.Clear();
                _envelope = null;
                RebuildAllLines();
                ApplyDisplay();

                var probe = await _reader.ReadHeadAsync(path, HugsLogEnvelopeParser.ProbeBytes);
                PathText = path;

                LogReadResult result;
                if (HugsLogEnvelopeParser.LooksLikeEnvelope(probe.Text))
                {
                    // A shared export wraps the log in a metadata envelope.
                    // Exports are small by construction (the publisher trims
                    // them), so the whole-file read here is cheap; feed the
                    // store the log body alone, so the display, filters and
                    // reports all see log content, with the sharer's own line
                    // numbers.
                    result = await _reader.ReadAsync(path);
                    if (!result.Found)
                    {
                        ShowReadFailure(path, result);
                        return;
                    }

                    _envelope = HugsLogEnvelopeParser.TryParse(result.Text);
                    _store.AppendText(_envelope?.BodyText ?? result.Text);
                }
                else
                {
                    // A plain log streams straight into the store in bounded
                    // memory: a multi-gigabyte file costs the store's cap, not
                    // the file size, and every line is still seen exactly once
                    // so eviction counters and line numbering stay exact.
                    _envelope = null;
                    result = await _reader.ReadIntoStoreAsync(path, _store);
                    if (!result.Found)
                    {
                        ShowReadFailure(path, result);
                        return;
                    }
                }

                _currentPath = path;
                RebuildAllLines();
                ApplyDisplay();

                SizeText = FormatSize(result.FileSize);
                ModifiedText = FormatModified(path);
                SyncSources(path, _envelope != null ? "shared export" : null);
                await RebuildDiagnosticsAsync(path);
                StatusMessage = _envelope != null
                    ? "Loaded shared log export — the publisher's metadata is summarized on the Summary tab."
                    : "Loaded.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Shows a failed read: the common failure path for both read modes.</summary>
        private void ShowReadFailure(string path, LogReadResult result)
        {
            SizeText = string.Empty;
            ModifiedText = string.Empty;
            LineCountText = "0 lines";
            ApplyDisplay();
            // Set after ApplyDisplay so the read failure is what stays visible.
            StatusMessage = result.ErrorMessage != null
                ? $"Could not read the log: {result.ErrorMessage}"
                : "Log file not found.";
        }

        private async Task SwitchSourceAsync(LogSourceOption option)
        {
            if (option.Path == _currentPath) return;
            await LoadAsync(option.Path);
        }

        /// <summary>
        /// Rebuilds the three report tabs. The display store keeps only the
        /// newest slice of a large log (it evicts oldest-first at its cap), so
        /// the log's head is read separately and merged with the retained window
        /// — otherwise the startup banner, version and mod list would vanish
        /// from the reports on any log bigger than the cap.
        /// </summary>
        private async Task RebuildDiagnosticsAsync(string path)
        {
            LogHeadResult head;
            try
            {
                head = await _reader.ReadHeadAsync(path, LogDiagnosticsWindow.HeadBytes);
            }
            catch (Exception)
            {
                head = new LogHeadResult { Found = false };
            }

            var window = LogDiagnosticsWindow.Build(
                HugsLogEnvelopeParser.Strip(head.Text),
                _store.FirstRetainedLineNumber,
                _store.Lines);
            Diagnostics.Rebuild(
                window,
                _pathService?.GetMajorGameVersion(),
                LogDiagnosticsWindow.DescribeScope(head.CoversWholeFile, _store.LinesEvicted),
                Stats,
                _envelope);
        }

        /// <summary>
        /// Rebuilds the source list from the files that exist right now. With a
        /// loaded path, that path stays selectable even when it is not one of the
        /// located files (a hand-opened log), and the combo follows the load.
        /// <paramref name="openedFileLabel"/> renames the hand-opened entry when
        /// the loaded file is a shared export rather than a plain log.
        /// </summary>
        private void RebuildSourceOptions(string? loadedPath = null, string? openedFileLabel = null)
        {
            var resolved = PlayerLogLocator.Resolve(
                _pathService?.GetConfigPath(),
                _pathService?.GetMajorGameVersion());

            var options = new List<LogSourceOption>();
            if (resolved.Current != null)
            {
                options.Add(new LogSourceOption("Player.log (this session)", resolved.Current));
            }

            if (resolved.Previous != null)
            {
                options.Add(new LogSourceOption("Player-prev.log (last session)", resolved.Previous));
            }

            if (loadedPath != null && !options.Any(o => o.Path == loadedPath))
            {
                options.Insert(0, new LogSourceOption(
                    Path.GetFileName(loadedPath) + $" ({openedFileLabel ?? "opened file"})", loadedPath));
            }

            SourceOptions.Clear();
            foreach (var option in options)
            {
                SourceOptions.Add(option);
            }

            if (loadedPath == null)
            {
                // No load to follow: drop a selection whose file just disappeared,
                // so the combo never advertises a file the reader cannot open.
                var selected = SelectedSourceOption;
                if (selected != null && !File.Exists(selected.Path))
                {
                    _syncingSource = true;
                    try { SelectedSourceOption = null; }
                    finally { _syncingSource = false; }
                }

                return;
            }

            _syncingSource = true;
            try
            {
                SelectedSourceOption = options.FirstOrDefault(o => o.Path == loadedPath);
            }
            finally
            {
                _syncingSource = false;
            }
        }

        private void SyncSources(string loadedPath, string? openedFileLabel = null) =>
            RebuildSourceOptions(loadedPath, openedFileLabel);

        /// <summary>
        /// Groups stored lines into whole messages once and classifies each, so
        /// filters, statistics and the text search all operate on messages
        /// (lead line plus indented continuations), not bare lines. Consecutive
        /// identical messages collapse into one entry carrying a repeat count.
        /// </summary>
        private void RebuildAllLines()
        {
            _allEntries.Clear();
            var stats = new LogStats();

            LogEntryViewModel? previous = null;
            foreach (var message in LogMessageGrouper.Group(_store.Lines))
            {
                var classification = LogLineClassifier.ClassifyMessage(message.Text);

                if (_collapseRepeats && previous != null && previous.Text == message.Text)
                {
                    // The run counts once (already tallied at its first message),
                    // so only the surviving entry's repeat count grows.
                    previous = previous.WithRepeat();
                    _allEntries[^1] = previous;
                    continue;
                }

                var entry = new LogEntryViewModel(message.Text, classification);
                _allEntries.Add(entry);
                stats.Add(classification);
                previous = entry;
            }

            Stats = stats;
            OnPropertyChanged(nameof(LoadedCount));
        }

        /// <summary>Rebuilds the displayed list from the current filter selections.</summary>
        private void ApplyDisplay()
        {
            var mode = _selectedFilterOption?.Mode ?? LogFilterMode.AllEntries;
            Lines.Clear();

            foreach (var entry in _allEntries)
            {
                if (!LogLineClassifier.MatchesFilter(entry.Classification, mode)) continue;
                if (!LogLineClassifier.MatchesTextFilter(entry.Text, LineFilter)) continue;
                Lines.Add(entry);
            }

            LineCountText = Lines.Count == _allEntries.Count
                ? $"{_allEntries.Count:N0} lines"
                : $"{Lines.Count:N0} of {_allEntries.Count:N0} lines";
            StatusMessage = Lines.Count == 0
                ? "No lines match the current filters."
                : "Loaded.";
            OnPropertyChanged(nameof(ShowLogPlaceholder));
            OnPropertyChanged(nameof(LoadedCount));
        }

        private static string FormatModified(string path)
        {
            try
            {
                return "Last modified " + File.GetLastWriteTime(path).ToString("G");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return string.Empty;
            }
        }

        private static string FormatSize(long bytes)
        {
            double size = bytes;
            foreach (var unit in new[] { "B", "KB", "MB", "GB", "TB" })
            {
                if (size < 1024) return FormattableString.Invariant($"{size:F1} {unit}");
                size /= 1024;
            }

            return FormattableString.Invariant($"{size:F1} PB");
        }
    }

    /// <summary>A filter mode with its display label, for the combo box and stat buttons.</summary>
    public sealed record LogFilterOption(string Label, LogFilterMode Mode);

    /// <summary>A log file the viewer can read, labeled for the source switcher.</summary>
    public sealed record LogSourceOption(string Label, string Path);
}
