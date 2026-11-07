#nullable enable
using System;
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
        }

        /// <summary>Classified lines bound to the virtualizing display list.</summary>
        public ObservableCollection<LogLineViewModel> Lines { get; } = new();

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

        public ICommand LoadDefaultCommand { get; }
        public ICommand RefreshCommand { get; }

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
                RebuildLines();

                SizeText = FormatSize(result.FileSize);
                LineCountText = $"{_store.LineCount:N0} lines";
                StatusMessage = "Loaded. Live tailing is not enabled yet.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void RebuildLines()
        {
            foreach (var line in _store.Lines)
            {
                Lines.Add(new LogLineViewModel(line, LogLineClassifier.Classify(line).Severity));
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
}
