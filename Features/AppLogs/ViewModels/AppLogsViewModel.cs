#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Features.AppLogs.Services;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.AppLogs.ViewModels
{
    /// <summary>
    /// Drives the app-logs window: the services that have written log files
    /// (one file per service per day), the messages of the selected service's
    /// newest day, the level filter, the text search and the export. Reading
    /// mirrors the Player.log viewer — whole messages, severity colors,
    /// collapsed repeats — over logs the app itself wrote.
    /// </summary>
    public class AppLogsViewModel : DialogViewModelBase<bool>
    {
        private readonly IConfigService? _configService;
        private readonly string? _appDataDirectory;
        private readonly IFileDialogService? _fileDialogService;

        private AppLogModuleItem? _selectedModule;
        private AppLogLevelFilter _selectedLevelFilter;
        private string _searchText = string.Empty;
        private bool _collapseRepeats = true;
        private bool _isLoading;
        private string _filePathText = "No log loaded";
        private string _statusMessage = "Pick a service to read its log.";
        private AppLogStats _stats = new(0, 0, 0, 0, 0, 0);

        public AppLogsViewModel(
            IConfigService? configService = null,
            string? appDataDirectory = null,
            IFileDialogService? fileDialogService = null)
            : base("App Logs")
        {
            _configService = configService;
            _appDataDirectory = appDataDirectory;
            _fileDialogService = fileDialogService;
            _selectedLevelFilter = LevelFilters[0];

            RefreshCommand = CreateAsyncCommand(RefreshAsync, () => !IsLoading, nameof(IsLoading));
            ExportCommand = CreateAsyncCommand(
                ExportAsync,
                () => !IsLoading && DisplayedCount > 0,
                nameof(IsLoading),
                nameof(DisplayedCount));
        }

        /// <summary>Level filter options for the combo; "All" first.</summary>
        public IReadOnlyList<AppLogLevelFilter> LevelFilters { get; } = new[]
        {
            new AppLogLevelFilter("All levels", null),
            new AppLogLevelFilter("Critical", AppLogLevel.Critical),
            new AppLogLevelFilter("Error", AppLogLevel.Error),
            new AppLogLevelFilter("Warning", AppLogLevel.Warning),
            new AppLogLevelFilter("Info", AppLogLevel.Info),
            new AppLogLevelFilter("Debug", AppLogLevel.Debug)
        };

        /// <summary>Services with log files on disk, in the order found.</summary>
        public ObservableCollection<AppLogModuleItem> Modules { get; } = new();

        /// <summary>Whole messages of the selected file, before filtering.</summary>
        public ObservableCollection<AppLogEntryViewModel> Messages { get; } = new();

        /// <summary>Messages the current filter and search leave, bound to the list.</summary>
        public ObservableCollection<AppLogEntryViewModel> Displayed { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public AppLogModuleItem? SelectedModule
        {
            get => _selectedModule;
            set
            {
                if (!SetProperty(ref _selectedModule, value)) return;
                LoadTask = value == null ? Task.CompletedTask : LoadSelectedAsync();
            }
        }

        /// <summary>
        /// The load started by the latest module selection. Tests await this
        /// instead of sleeping; the view ignores it (the load updates state).
        /// </summary>
        public Task? LoadTask { get; private set; }

        public AppLogLevelFilter SelectedLevelFilter
        {
            get => _selectedLevelFilter;
            set
            {
                if (!SetProperty(ref _selectedLevelFilter, value) || value == null) return;
                ApplyDisplay();
            }
        }

        /// <summary>Text the messages must contain, applied as typed (called from the view, debounced).</summary>
        public string SearchText
        {
            get => _searchText;
            private set => SetProperty(ref _searchText, value);
        }

        /// <summary>Consecutive identical messages display as one, annotated with their repeat count.</summary>
        public bool CollapseRepeats
        {
            get => _collapseRepeats;
            set
            {
                if (!SetProperty(ref _collapseRepeats, value)) return;
                RebuildMessages();
                ApplyDisplay();
            }
        }

        public string FilePathText
        {
            get => _filePathText;
            private set => SetProperty(ref _filePathText, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public AppLogStats Stats
        {
            get => _stats;
            private set => SetProperty(ref _stats, value);
        }

        /// <summary>Messages after filtering; drives Export's enabled state.</summary>
        public int DisplayedCount => Displayed.Count;

        /// <summary>Line-count header text over the current view.</summary>
        public string LineCountText
        {
            get
            {
                if (Messages.Count == 0) return "0 messages";
                return Displayed.Count == Messages.Count
                    ? $"{Messages.Count:N0} messages"
                    : $"{Displayed.Count:N0} of {Messages.Count:N0} messages";
            }
        }

        /// <summary>True while nothing is loaded — the view shows its empty-state hint.</summary>
        public bool ShowPlaceholder => Messages.Count == 0;

        public void ApplySearch(string? value)
        {
            var normalized = value ?? string.Empty;
            if (SearchText == normalized) return;
            SearchText = normalized;
            ApplyDisplay();
        }

        /// <summary>Rescans the logs directory and rebuilds the module list.</summary>
        public async Task RefreshAsync()
        {
            IsLoading = true;
            try
            {
                await Task.Yield();

                var logsDirectory = AppLogLocator.ResolveLogsDirectory(_configService, _appDataDirectory);
                var files = AppLogLocator.Enumerate(logsDirectory);

                var groups = files
                    .GroupBy(f => f.Module, StringComparer.Ordinal)
                    .Select(g => new AppLogModuleItem(g.Key, g.ToList()))
                    .OrderByDescending(m => m.Newest.Date)
                    .ThenBy(m => m.Module, StringComparer.Ordinal)
                    .ToList();

                Modules.Clear();
                foreach (var group in groups)
                {
                    Modules.Add(group);
                }

                if (Modules.Count == 0)
                {
                    SelectedModule = null;
                    Messages.Clear();
                    RebuildStats();
                    ApplyDisplay();
                    FilePathText = logsDirectory;
                    StatusMessage = "No app log files found — the app writes one per service per day.";
                    return;
                }

                // Keep the selection when it still exists, else take the newest.
                var current = _selectedModule != null && Modules.Any(m => m.Module == _selectedModule.Module)
                    ? Modules.First(m => m.Module == _selectedModule.Module)
                    : Modules[0];

                if (ReferenceEquals(current, _selectedModule))
                {
                    LoadTask = LoadSelectedAsync();
                    await LoadTask;
                }
                else
                {
                    SelectedModule = current;
                    await (LoadTask ?? Task.CompletedTask);
                }
                // The load itself sets the status message, so an empty file
                // says so instead of being overwritten with "Loaded." here.
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Reads the selected service's newest log file into messages.</summary>
        private async Task LoadSelectedAsync()
        {
            var module = _selectedModule;
            if (module == null) return;

            IsLoading = true;
            try
            {
                await Task.Yield();

                var file = module.Newest;
                var entries = await Task.Run(() => AppLogReader.ReadFile(file.Path));

                FilePathText = file.Path;
                RebuildMessages(entries);
                RebuildStats(entries);
                ApplyDisplay();

                StatusMessage = entries.Count == 0
                    ? $"The log file for {module.Module} is empty."
                    : "Loaded.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private readonly System.Collections.Generic.List<AppLogEntry> _loadedEntries = new();

        private void RebuildMessages(System.Collections.Generic.IReadOnlyList<AppLogEntry>? entries = null)
        {
            if (entries != null)
            {
                _loadedEntries.Clear();
                _loadedEntries.AddRange(entries);
            }

            Messages.Clear();

            AppLogEntryViewModel? previous = null;
            foreach (var entry in _loadedEntries)
            {
                if (_collapseRepeats && previous != null && previous.Entry.Text == entry.Text && previous.Level == entry.Level)
                {
                    previous = previous.WithRepeat();
                    Messages[^1] = previous;
                    continue;
                }

                var item = new AppLogEntryViewModel(entry);
                Messages.Add(item);
                previous = item;
            }

            OnPropertyChanged(nameof(LineCountText));
            OnPropertyChanged(nameof(ShowPlaceholder));
        }

        private void RebuildStats(System.Collections.Generic.IReadOnlyList<AppLogEntry>? entries = null)
        {
            Stats = entries == null ? new AppLogStats(0, 0, 0, 0, 0, 0) : AppLogReader.CountStats(entries);
        }

        private void ApplyDisplay()
        {
            var level = _selectedLevelFilter?.Level;
            var search = SearchText;

            Displayed.Clear();
            foreach (var message in Messages)
            {
                if (level.HasValue && message.Level != level.Value) continue;
                if (search.Length > 0 &&
                    message.Entry.Text.Contains(search, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                Displayed.Add(message);
            }

            OnPropertyChanged(nameof(DisplayedCount));
            OnPropertyChanged(nameof(LineCountText));
            if (Messages.Count > 0 && Displayed.Count == 0)
            {
                StatusMessage = "No messages match the current filter.";
            }
        }

        /// <summary>Writes the currently displayed messages to a file.</summary>
        public async Task ExportAsync()
        {
            if (Displayed.Count == 0) return;

            if (_fileDialogService == null)
            {
                StatusMessage = "Export needs the file dialog service.";
                return;
            }

            var (success, path) = await _fileDialogService.ShowSaveFileDialogAsync(
                "Export the displayed messages",
                "Text files|*.txt|Log files|*.log|All files|*.*",
                defaultExtension: "txt",
                defaultFileName: "RimSharp-app-log-export.txt");
            if (!success || string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "Export cancelled.";
                return;
            }

            try
            {
                var text = string.Join(Environment.NewLine, Displayed.Select(m => m.DisplayText)) + Environment.NewLine;
                await File.WriteAllTextAsync(path, text);
                StatusMessage = $"Exported {Displayed.Count:N0} messages to {path}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
        }
    }
}
