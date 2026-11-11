#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs;
using RimSharp.Services.ModManagement;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.Saves.ViewModels
{
    /// <summary>
    /// Drives the read-only saves dialog: the list of save files and the
    /// details of the selected save. The list is built from the file metadata
    /// alone - a save is parsed only when it is selected. The dialog never
    /// writes, moves or deletes a save.
    /// </summary>
    public class SavesDialogViewModel : DialogViewModelBase<bool>
    {
        /// <summary>How many saves are listed inline before the "+N more" expander.</summary>
        public const int InlineRowLimit = 8;

        /// <summary>
        /// The save that was selected last. A dialog that is opened again
        /// restores it, as long as the file is still there.
        /// </summary>
        public static string? LastSelectedPath { get; set; }

        /// <summary>
        /// Headers that were already read. A re-opened dialog shows the last
        /// save without parsing it again; a refresh drops the cache so every
        /// file is read from disk once more.
        /// </summary>
        private static readonly ConcurrentDictionary<string, SaveHeader> HeaderCache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ISaveHeaderService _saveHeaderService;
        private readonly IModListManager? _modListManager;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly CancellationToken _token;

        private SaveRowViewModel? _selectedSave;
        private bool _isLoadingList;
        private bool _isExpanded;
        private bool _rebuildingVisibleRows;
        private string _statusMessage = "Reading save folder…";

        // The scan of the colonist names: it runs in the background while a
        // save is selected and a new selection supersedes the running one.
        private CancellationTokenSource? _colonistScanCancellation;
        private int _colonistScanGeneration;

        public SavesDialogViewModel(
            ISaveHeaderService saveHeaderService,
            IModListManager? modListManager = null)
            : base("Saves")
        {
            _saveHeaderService = saveHeaderService ?? throw new ArgumentNullException(nameof(saveHeaderService));
            _modListManager = modListManager;
            _token = _cancellation.Token;

            RefreshCommand = CreateAsyncCommand(
                () => RefreshAsync(_token),
                () => !IsLoadingList,
                nameof(IsLoadingList));
        }

        #region Collections

        /// <summary>Every save of the folder, newest first.</summary>
        public ObservableCollection<SaveRowViewModel> Saves { get; } = new();

        /// <summary>The saves that are listed inline - at most <see cref="InlineRowLimit"/>.</summary>
        public ObservableCollection<SaveRowViewModel> InlineSaves { get; } = new();

        /// <summary>The saves the "+N more" expander reveals.</summary>
        public ObservableCollection<SaveRowViewModel> ExpandedSaves { get; } = new();

        /// <summary>The saves the list shows - the inline ones plus the expanded ones.</summary>
        public ObservableCollection<SaveRowViewModel> VisibleSaves { get; } = new();

        #endregion

        /// <summary>Reloads the save folder.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>True while the save folder is being read.</summary>
        public bool IsLoadingList
        {
            get => _isLoadingList;
            private set => SetProperty(ref _isLoadingList, value);
        }

        /// <summary>True when the folder holds no save at all.</summary>
        public bool ShowPlaceholder => !IsLoadingList && Saves.Count == 0;

        /// <summary>True when there are more saves than the inline list shows.</summary>
        public bool HasMoreSaves => ExpandedSaves.Count > 0;

        /// <summary>The label of the expander: "+{N} more".</summary>
        public string MoreCountText => $"+{ExpandedSaves.Count} more";

        /// <summary>The label of the count chip: "{N} saves".</summary>
        public string CountText => $"{Saves.Count} saves";

        /// <summary>True when the expander reveals the saves beyond the inline ones.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (!SetProperty(ref _isExpanded, value)) return;
                ApplyExpansion();
            }
        }

        /// <summary>The last thing the dialog did - shown in the status line.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>The save whose details are shown.</summary>
        public SaveRowViewModel? SelectedSave
        {
            get => _selectedSave;
            set
            {
                // The list clears its selection while its rows are rebuilt -
                // that must not wipe the details of the selected save.
                if (_rebuildingVisibleRows) return;
                if (!SetProperty(ref _selectedSave, value)) return;

                LastSelectedPath = value?.Path;

                if (value == null)
                {
                    // Nothing is selected any more - a scan that is still
                    // running must not land on the empty panel.
                    CancelColonistNamesScan();
                    Details.Clear();
                    LoadTask = Task.CompletedTask;
                    return;
                }

                LoadTask = LoadSelectedAsync(value, _token);
            }
        }

        /// <summary>
        /// The read of the selected save. Tests await this instead of sleeping;
        /// the view ignores it (the read updates the state).
        /// </summary>
        public Task? LoadTask { get; private set; }

        /// <summary>
        /// The scan for the colonist names. It runs in the background while a
        /// save is selected. Tests await this instead of sleeping; the view
        /// ignores it (the scan updates the state).
        /// </summary>
        public Task? ColonistNamesTask { get; private set; }

        /// <summary>The details panel of the selected save.</summary>
        public SaveDetailsViewModel Details { get; } = new();

        /// <summary>
        /// Reads the save folder and rebuilds the list. The selection survives:
        /// the save that was selected is kept when it still exists, otherwise
        /// the newest save is selected.
        /// </summary>
        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (IsLoadingList) return;

            IsLoadingList = true;
            StatusMessage = "Reading save folder…";

            try
            {
                await Task.Yield();

                // A refresh looks at the files again - nothing is served from the cache.
                HeaderCache.Clear();

                var entries = await _saveHeaderService.ListSavesAsync(cancellationToken);

                _rebuildingVisibleRows = true;
                try
                {
                    Saves.Clear();
                    InlineSaves.Clear();
                    ExpandedSaves.Clear();
                    VisibleSaves.Clear();

                    foreach (var entry in entries)
                    {
                        Saves.Add(new SaveRowViewModel(entry));
                    }

                    foreach (var row in Saves.Take(InlineRowLimit))
                    {
                        InlineSaves.Add(row);
                        VisibleSaves.Add(row);
                    }

                    foreach (var row in Saves.Skip(InlineRowLimit))
                    {
                        ExpandedSaves.Add(row);
                    }
                }
                finally
                {
                    _rebuildingVisibleRows = false;
                }

                OnPropertyChanged(nameof(ShowPlaceholder));
                OnPropertyChanged(nameof(HasMoreSaves));
                OnPropertyChanged(nameof(MoreCountText));
                OnPropertyChanged(nameof(CountText));

                StatusMessage = Saves.Count == 0
                    ? "No saves found."
                    : $"Read the save folder — {Saves.Count} saves found.";

                RestoreSelection();
            }
            finally
            {
                IsLoadingList = false;
            }
        }

        /// <summary>
        /// Keeps the selected save, or the one that was selected last, or the
        /// newest save - in that order.
        /// </summary>
        private void RestoreSelection()
        {
            string? targetPath = SelectedSave?.Path ?? LastSelectedPath;

            var restored = targetPath == null
                ? null
                : Saves.FirstOrDefault(row => string.Equals(row.Path, targetPath, StringComparison.OrdinalIgnoreCase));

            var next = restored ?? Saves.FirstOrDefault();

            if (!ReferenceEquals(next, SelectedSave))
            {
                SelectedSave = next;
                return;
            }

            if (next != null)
            {
                // The same save is selected again - read it once more, the
                // files may have changed in the meantime.
                LoadTask = LoadSelectedAsync(next, _token);
            }
        }

        /// <summary>
        /// Shows the saves of the expander in the list - and takes them out
        /// again when it is closed. Nothing is parsed here - the list only
        /// shows what the file system already knows.
        /// </summary>
        private void ApplyExpansion()
        {
            _rebuildingVisibleRows = true;
            try
            {
                if (IsExpanded)
                {
                    foreach (var row in ExpandedSaves)
                    {
                        if (!VisibleSaves.Contains(row))
                        {
                            VisibleSaves.Add(row);
                        }
                    }
                }
                else
                {
                    foreach (var row in ExpandedSaves)
                    {
                        VisibleSaves.Remove(row);
                    }
                }
            }
            finally
            {
                _rebuildingVisibleRows = false;
            }
        }

        /// <summary>Reads the header of the selected save and fills the details.</summary>
        private async Task LoadSelectedAsync(SaveRowViewModel row, CancellationToken cancellationToken)
        {
            // A new selection supersedes the scan that is still running: the
            // names of the old save must never land on the new one.
            CancelColonistNamesScan();

            Details.BeginLoading(row.Name, row.Path);
            StatusMessage = "Reading this save…";

            try
            {
                var header = await GetHeaderAsync(row.Path, cancellationToken);
                if (header == null)
                {
                    // The verdict of an unreadable save is unknown, not compatible.
                    Details.ApplyCompat(
                        SaveCompat.Unreadable,
                        0,
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>());

                    // A file that vanished between the listing and the click is
                    // not a broken save - it says so.
                    bool stillThere = File.Exists(row.Path);
                    Details.ApplyError(
                        "Could not read this save.",
                        stillThere
                            ? "The save is corrupt or in a format this reader can't parse."
                            : "Save no longer exists — it may have been deleted or moved. Refresh the list.");

                    StatusMessage = "Could not read this save.";
                    return;
                }

                ApplyCompatSection(header, ComputeCompat(header));

                var details = await _saveHeaderService.ReadDetailsAsync(row.Path, cancellationToken);
                if (details == null)
                {
                    Details.ApplyDetails(null, header.GameVersion);
                    StatusMessage = "Only the header of this save could be read.";
                    return;
                }

                // The DLCs are not in the head window - they are the official
                // packages the save was played with, taken from its meta block.
                Details.ApplyDetails(
                    details with { ActiveDlcNames = ISaveHeaderService.ResolveActiveDlcNames(header.ModIds) },
                    header.GameVersion);

                StatusMessage = $"Read {row.Name}.";

                // The save is readable - the colonists are read in the background.
                // The details above are already shown; the scan never delays them.
                StartColonistNamesScan(row, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The dialog was closed while the save was being read.
            }
            catch (Exception)
            {
                Details.ApplyCompat(
                    SaveCompat.Unreadable,
                    0,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>());
                Details.ApplyError(
                    "Could not read this save.",
                    "The save is corrupt or in a format this reader can't parse.");
                StatusMessage = "Could not read this save.";
            }
            finally
            {
                Details.EndLoading();
            }
        }

        /// <summary>
        /// Starts the scan for the colonists. The scan is a whole file read -
        /// it runs in the background and never delays the details.
        /// The user asked for it by selecting the save.
        /// </summary>
        private void StartColonistNamesScan(SaveRowViewModel row, CancellationToken cancellationToken)
        {
            // A scan that is still running belongs to the previous selection.
            CancelColonistNamesScan();

            int generation = _colonistScanGeneration;

            _colonistScanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Details.BeginColonistNamesLoad();

            ColonistNamesTask = RunColonistNamesScanAsync(row, generation,
                _colonistScanCancellation.Token);
        }

        /// <summary>
        /// Runs the scan and puts the names into the panel - but only when the
        /// scan still belongs to the selected save.
        /// </summary>
        private async Task RunColonistNamesScanAsync(SaveRowViewModel row, int generation,
            CancellationToken cancellationToken)
        {
            try
            {
                var names = await _saveHeaderService.ReadColonistNamesAsync(row.Path, cancellationToken);
                var found = names ?? Array.Empty<SaveColonist>();

                // The selection changed while the scan ran - the result belongs
                // to a save that is no longer shown and is dropped.
                if (generation != _colonistScanGeneration)
                {
                    return;
                }

                if (!string.Equals(row.Path, SelectedSave?.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // The names are shown in the Colonists section - the status line
                // keeps what the read of the save said.
                Details.ApplyColonistNames(found);
            }
            catch (OperationCanceledException)
            {
                // The dialog was closed or another save was selected.
            }
            catch (Exception)
            {
                if (generation != _colonistScanGeneration)
                {
                    return;
                }

                // The scan failed - the section says so instead of showing a
                // made-up name.
                Details.ApplyColonistNames(Array.Empty<SaveColonist>());
            }
        }

        /// <summary>
        /// Cancels the scan that is still running. Every result of a cancelled
        /// scan is dropped - it belongs to a save that is no longer shown.
        /// </summary>
        private void CancelColonistNamesScan()
        {
            try
            {
                _colonistScanCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already gone - nothing to cancel.
            }

            _colonistScanCancellation?.Dispose();
            _colonistScanCancellation = null;

            // The generation counts the selections - a result of an older
            // generation is never applied.
            _colonistScanGeneration++;
        }

        /// <summary>Fills the compatibility block of the details panel.</summary>
        private void ApplyCompatSection(SaveHeader header, SaveCompat compat)
        {
            var (active, installed) = ResolveModContext();

            var diff = _saveHeaderService.Diff(header, active);
            IReadOnlyList<string>? added = diff.Added;
            IReadOnlyList<string>? removed = diff.Removed;
            added ??= Array.Empty<string>();
            removed ??= Array.Empty<string>();

            var installedIds = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
            var saveIds = header.ModIds ?? Array.Empty<string>();
            var missing = saveIds.Where(id => !installedIds.Contains(id)).ToList();

            Details.ApplyCompat(compat, saveIds.Count, added, removed, missing);
        }

        /// <summary>
        /// The active and the installed mod ids the saves are compared against.
        /// Without the mod list there is nothing to compare against - the
        /// verdict stays unknown then.
        /// </summary>
        private (IReadOnlyCollection<string> Active, IReadOnlyCollection<string> Installed) ResolveModContext()
        {
            if (_modListManager == null)
            {
                return (Array.Empty<string>(), Array.Empty<string>());
            }

            var installed = _modListManager.GetAllMods()
                .Select(mod => mod.PackageId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            var active = _modListManager.VirtualActiveMods
                .Select(entry => entry.Mod.PackageId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            return (active, installed);
        }

        /// <summary>Compares a save against the active and the installed mods.</summary>
        private SaveCompat ComputeCompat(SaveHeader header)
        {
            var (active, installed) = ResolveModContext();

            if (installed.Count == 0)
            {
                // The mod list is not known - the compatibility cannot be judged.
                return SaveCompat.Unknown;
            }

            return _saveHeaderService.Compare(header, active, installed);
        }

        /// <summary>Reads a header, serving a header that was read before from the cache.</summary>
        private async Task<SaveHeader?> GetHeaderAsync(string path, CancellationToken cancellationToken)
        {
            if (HeaderCache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var header = await _saveHeaderService.ReadHeaderAsync(path, cancellationToken);
            if (header != null)
            {
                HeaderCache[path] = header;
            }

            return header;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // The dialog is closed - a scan that is still running is stopped.
                CancelColonistNamesScan();

                try
                {
                    _cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already gone - nothing to cancel.
                }

                _cancellation.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
