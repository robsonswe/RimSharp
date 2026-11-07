#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    /// <see cref="LogContentStore"/> and exposes classified lines for display.
    /// </summary>
    public class LogViewerViewModel : DialogViewModelBase<bool>
    {
        private readonly IPathService? _pathService;
        private readonly PlayerLogReader _reader;
        private readonly LogContentStore _store = new();
        private readonly List<LogEntryViewModel> _allEntries = new();

        private string? _currentPath;
        private long _tailOffset;
        private bool _isLoading;

        public LogViewerViewModel(IPathService? pathService = null, PlayerLogReader? reader = null)
            : base("Log Viewer")
        {
            _pathService = pathService;
            _reader = reader ?? new PlayerLogReader();
            LoadDefaultCommand = CreateAsyncCommand(LoadDefaultAsync, () => !IsLoading, nameof(IsLoading));
            RefreshCommand = CreateAsyncCommand(RefreshAsync, () => !IsLoading, nameof(IsLoading));
            SelectFilterCommand = CreateCommand<LogFilterOption>(option =>
            {
                SelectedFilterOption = option;
            });
            _selectedFilterOption = FilterOptions[0];
        }

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

        /// <summary>Whole messages bound to the virtualizing display list (after filtering).</summary>
        public ObservableCollection<LogEntryViewModel> Lines { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
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

        private string _lineCountText = "0 lines";
        public string LineCountText
        {
            get => _lineCountText;
            private set => SetProperty(ref _lineCountText, value);
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
        public ICommand SelectFilterCommand { get; }

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

        /// <summary>Reads any log file (also the path for load-from-file later).</summary>
        public async Task LoadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            IsLoading = true;
            try
            {
                Lines.Clear();
                _store.Clear();

                var result = await _reader.ReadAsync(path);
                PathText = path;

                if (!result.Found)
                {
                    SizeText = string.Empty;
                    LineCountText = "0 lines";
                    StatusMessage = result.ErrorMessage != null
                        ? $"Could not read the log: {result.ErrorMessage}"
                        : "Log file not found.";
                    return;
                }

                _currentPath = path;
                _tailOffset = result.BytesRead;
                _store.AppendText(result.Text);
                RebuildAllLines();
                ApplyDisplay();

                SizeText = FormatSize(result.FileSize);
                StatusMessage = "Loaded. Live tailing is not enabled yet.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Groups stored lines into whole messages once and classifies each, so
        /// filters, statistics and the text search all operate on messages
        /// (lead line plus indented continuations), not bare lines.
        /// </summary>
        private void RebuildAllLines()
        {
            _allEntries.Clear();
            var stats = new LogStats();
            foreach (var message in LogMessageGrouper.Group(_store.Lines))
            {
                var classification = LogLineClassifier.ClassifyMessage(message.Text);
                _allEntries.Add(new LogEntryViewModel(message.Text, classification));
                stats.Add(classification);
            }
            Stats = stats;
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
                : "Loaded. Live tailing is not enabled yet.";
            OnPropertyChanged(nameof(ShowLogPlaceholder));
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
}
