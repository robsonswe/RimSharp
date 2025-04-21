using Microsoft.Win32;
using RimSharp.AppDir.Dialogs;
using RimSharp.Core.Extensions;
using RimSharp.Features.ModManager.Dialogs.MissingMods;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Features.WorkshopDownloader.Services;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // Keep for Application.Current
using System.Xml.Linq;

namespace RimSharp.Infrastructure.Mods.IO
{
    public class ModListIOService : IModListIOService
    {
        private readonly IPathService _pathService;
        private readonly IModListManager _modListManager;
        private readonly IDialogService _dialogService; // Added
        private readonly IModDictionaryService _modDictionaryService;
        private readonly ISteamApiClient _steamApiClient;
        private readonly IDownloadQueueService _downloadQueueService;
        private readonly IApplicationNavigationService _navigationService;
        private readonly ILoggerService _logger; // Assuming logging is desired
        private const int MaxParallelApiOperations = 10; // Max parallel downloads/API checks

        // Updated Constructor
        public ModListIOService(
            IPathService pathService,
            IModListManager modListManager,
            IDialogService dialogService,
            IModDictionaryService modDictionaryService,
            ISteamApiClient steamApiClient,
            IDownloadQueueService downloadQueueService,
            IApplicationNavigationService navigationService,
            ILoggerService logger)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService)); // Added
            _modDictionaryService = modDictionaryService ?? throw new ArgumentNullException(nameof(modDictionaryService));
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        }

        public async Task ImportModListAsync()
        {
            try
            {
                // Ensure Lists directory exists
                var listsDirectory = await EnsureListsDirectoryAsync();

                // Show file dialog
                var filePath = await ShowFileDialogAsync(listsDirectory, FileDialogType.Open);
                if (string.IsNullOrEmpty(filePath))
                    return;

                Debug.WriteLine($"Importing mod list from: {filePath}");

                // Load and parse XML file
                var activeModIds = await ParseModListFileAsync(filePath);
                if (activeModIds is null || !activeModIds.Any())
                    return;

                // Update mods
                var allMods = _modListManager.GetAllMods().ToList();

                // Check which mods are missing
                var availableModIds = allMods
                    .Where(m => !string.IsNullOrEmpty(m.PackageId))
                    .Select(m => m.PackageId.ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missingModIds = activeModIds
                    .Where(id => !availableModIds.Contains(id))
                    .ToList();

                // Only pass available mod IDs to the manager
                var availableActiveModIds = activeModIds
                    .Where(id => availableModIds.Contains(id))
                    .ToList();

                await Task.Run(() => _modListManager.Initialize(allMods, availableActiveModIds));

                // Display appropriate message with missing mods if any
                DisplayImportResultsAsync(Path.GetFileName(filePath), availableActiveModIds.Count, missingModIds);
            }
            catch (Exception ex)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Import Error", $"An unexpected error occurred during import: {ex.Message}");
                // -------------------------
            }
        }

        public async Task ExportModListAsync(IEnumerable<ModItem> activeMods)
        {
            if (activeMods is null)
                throw new ArgumentNullException(nameof(activeMods));

            try
            {
                // Ensure Lists directory exists
                var listsDirectory = await EnsureListsDirectoryAsync();

                // Show file dialog
                var filePath = await ShowFileDialogAsync(listsDirectory, FileDialogType.Save);
                if (string.IsNullOrEmpty(filePath))
                    return;

                Debug.WriteLine($"Exporting mod list to: {filePath}");

                // Filter out mods without package IDs before saving
                var validActiveMods = activeMods.Where(m => !string.IsNullOrEmpty(m.PackageId)).ToList();

                // Create XML document with active mods
                await SaveModListFileAsync(filePath, validActiveMods);

                // --- Replaced MessageBox ---
                _dialogService.ShowInformation("Export Successful", $"Mod list exported successfully to {Path.GetFileName(filePath)}!");
                // -------------------------
            }
            catch (UnauthorizedAccessException)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Export Error", "Error: Permission denied when saving the file.");
                // -------------------------
            }
            catch (Exception ex)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Export Error", $"An unexpected error occurred during export: {ex.Message}");
                // -------------------------
            }
        }

        #region Helper Methods

        private async Task<string> EnsureListsDirectoryAsync()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string listsDirectory = Path.Combine(appDirectory, "Lists");

            if (!Directory.Exists(listsDirectory))
            {
                await Task.Run(() => Directory.CreateDirectory(listsDirectory));
                Debug.WriteLine($"Created Lists directory at: {listsDirectory}");
            }

            return listsDirectory;
        }

        private enum FileDialogType { Open, Save }

        private Task<string> ShowFileDialogAsync(string initialDirectory, FileDialogType dialogType)
        {
            return Task.Run(() =>
            {
                string result = null;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (dialogType == FileDialogType.Open)
                    {
                        var openFileDialog = new OpenFileDialog
                        {
                            Title = "Import Mod List",
                            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                            InitialDirectory = initialDirectory,
                            CheckFileExists = true
                        };

                        if (openFileDialog.ShowDialog() == true)
                        {
                            result = openFileDialog.FileName;
                        }
                    }
                    else // Save dialog
                    {
                        var saveFileDialog = new SaveFileDialog
                        {
                            Title = "Export Mod List",
                            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                            InitialDirectory = initialDirectory,
                            DefaultExt = ".xml",
                            FileName = $"ModList_{DateTime.Now:yyyyMMdd}.xml"
                        };

                        if (saveFileDialog.ShowDialog() == true)
                        {
                            result = saveFileDialog.FileName;
                        }
                    }
                });

                return result;
            });
        }

        private async Task<List<string>> ParseModListFileAsync(string filePath)
        {
            XDocument doc;
            try
            {
                doc = await Task.Run(() => XDocument.Load(filePath));
            }
            catch (Exception ex)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Import Error", $"Error loading XML file: {ex.Message}");
                // -------------------------
                return null;
            }

            var activeModsElement = doc.Root?.Element("activeMods");
            if (activeModsElement is null)
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowError("Invalid File Format", "The selected file does not contain a valid mod list format.");
                // -------------------------
                return null;
            }

            var activeModIds = activeModsElement.Elements("li")
                .Select(e => e.Value.ToLowerInvariant())
                .ToList();

            if (!activeModIds.Any())
            {
                // --- Replaced MessageBox ---
                _dialogService.ShowWarning("Import Warning", "The file contains an empty mod list.");
                // -------------------------
                return null;
            }

            return activeModIds;
        }

        private async Task SaveModListFileAsync(string filePath, IEnumerable<ModItem> activeMods)
        {
            var doc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.0"),
                    new XElement("activeMods")
                )
            );

            var activeModsElement = doc.Root.Element("activeMods");

            foreach (var mod in activeMods)
            {
                if (!string.IsNullOrEmpty(mod.PackageId))
                {
                    activeModsElement.Add(new XElement("li", mod.PackageId.ToLowerInvariant()));
                }
                else
                {
                    Debug.WriteLine($"Warning: Mod '{mod.Name}' has no PackageId and was not exported.");
                }
            }

            await Task.Run(() => doc.Save(filePath));
        }

        private async Task DisplayImportResultsAsync(string fileName, int importedCount, List<string> missingModIds)
        {
            if (missingModIds.Count == 0)
            {
                _dialogService.ShowInformation(
                    "Import Successful",
                    $"Successfully imported mod list from {fileName} with {importedCount} active mods.");
                return;
            }

            var messageBuilder = new System.Text.StringBuilder();
            messageBuilder.AppendLine($"RimSharp imported {fileName} list.");
            messageBuilder.AppendLine($"Successfully activated {importedCount} installed mods.");
            messageBuilder.AppendLine("Do you want to try downloading the following missing mods?");
            messageBuilder.AppendLine();

            foreach (var modId in missingModIds)
            {
                messageBuilder.AppendLine($"- {modId}");
            }

            messageBuilder.AppendLine();

            // Show a confirmation dialog offering to download
            var confirmResult = _dialogService.ShowConfirmation(
                "Import Partially Successful",
                messageBuilder.ToString(),
                showCancel: true); // OK = Yes, Cancel = No

            if (confirmResult == MessageDialogResult.OK || confirmResult == MessageDialogResult.Yes)
            {
                _logger.LogInfo($"User chose to download {missingModIds.Count} missing mods from import.", nameof(ModListIOService));
                // User wants to download, proceed to selection dialog logic
                await HandleMissingModDownloadRequestAsync(missingModIds);
            }
            else
            {
                _logger.LogInfo("User declined to download missing mods from import.", nameof(ModListIOService));
                // User cancelled, maybe show the simple list again for copying if desired?
                var finalMessage = new StringBuilder();
                _dialogService.ShowInformation(
                    "Import Successful",
                    $"Successfully imported mod list from {fileName} with {importedCount} active mods.");
            }
        }
        // --- Updated Method to Handle Download Request ---
        private async Task HandleMissingModDownloadRequestAsync(List<string> missingModIds)
        {
            if (missingModIds == null || !missingModIds.Any()) return;

            List<MissingModGroupViewModel> groups = new List<MissingModGroupViewModel>();
            List<string> unknownIds = new List<string>();
            Dictionary<string, ModDictionaryEntry> allEntries = null;

            ProgressDialogViewModel progressDialog = null;
            CancellationTokenSource cts = new CancellationTokenSource(); // For potential cancellation

            try
            {
                // Show initial progress (might be quick)
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                        "Preparing Download Options",
                        "Loading mod dictionary...",
                        canCancel: true, // Allow cancelling this prep stage
                        isIndeterminate: true,
                        cts: cts,
                        closeable: false);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog not created");


                // Load *all* dictionary entries once
                await Task.Run(() => // Run dictionary loading in background
                {
                    allEntries = _modDictionaryService.GetAllEntries();
                }, cts.Token); // Pass token

                cts.Token.ThrowIfCancellationRequested(); // Check cancellation after loading

                // Update progress
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog.Message = "Matching missing mods...");

                // Filter and group (this should be quick)
                await Task.Run(() =>
                {
                    var allEntriesList = allEntries.Values.ToList(); // Work with a list

                    foreach (var missingId in missingModIds.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        var normalizedMissingId = missingId.ToLowerInvariant();

                        // Find matches in the loaded dictionary entries
                        var matchingEntries = allEntriesList
                            .Where(entry => !string.IsNullOrEmpty(entry.PackageId) &&
                                           entry.PackageId.Equals(normalizedMissingId, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (matchingEntries.Any())
                        {
                            var group = new MissingModGroupViewModel(missingId); // Use original casing for display
                            foreach (var entry in matchingEntries.OrderBy(e => e.Name)) // Sort variants by name
                            {
                                group.Variants.Add(new MissingModVariantViewModel(entry));
                            }

                            // --- START: Updated Default Selection ---
                            // Select the *first published* variant by default.
                            // If none are published, leave SelectedVariant as null.
                            group.SelectedVariant = group.Variants
                                                        .FirstOrDefault(v => v.IsPublished);
                            // --- END: Updated Default Selection ---

                            groups.Add(group);
                        }
                        else
                        {
                            unknownIds.Add(missingId);
                            _logger.LogDebug($"Missing mod ID '{missingId}' not found in dictionary.", nameof(ModListIOService));
                        }
                    }
                }, cts.Token); // Pass token

                cts.Token.ThrowIfCancellationRequested(); // Check after grouping

                // Close the preparation progress dialog
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.CompleteOperation("Ready."));

                // Show the selection dialog
                MissingModSelectionDialogOutput selectionResult = null;
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    var selectionViewModel = new MissingModSelectionDialogViewModel(groups, unknownIds);
                    selectionResult = _dialogService.ShowMissingModSelectionDialog(selectionViewModel);
                });

                if (selectionResult?.Result == MissingModSelectionResult.Download && selectionResult.SelectedSteamIds.Any())
                {
                    _logger.LogInfo($"User selected {selectionResult.SelectedSteamIds.Count} published mod variants to download.", nameof(ModListIOService));
                    // --- Trigger the actual download process ---
                    await QueueModsForDownloadAsync(selectionResult.SelectedSteamIds, CancellationToken.None); // Use CancellationToken.None for now, or pass a real one if needed
                }
                else
                {
                    _logger.LogInfo("User cancelled or did not select any (published) mods in the missing mod selection dialog.", nameof(ModListIOService));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Preparation for missing mod download was cancelled.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    progressDialog?.ForceClose();
                    _dialogService.ShowWarning("Operation Cancelled", "Preparing download options was cancelled.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Error preparing missing mod download options.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    progressDialog?.ForceClose();
                    _dialogService.ShowError("Error", $"Failed to prepare download options: {ex.Message}");
                });
            }
            finally
            {
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose()); // Ensure closed
                cts?.Dispose();
            }
        }

        // --- New Reusable Method to Queue Mods (Similar to ExecuteRedownloadModsAsync) ---
        // No changes needed in this method for the 'Published' logic, as it deals with Steam IDs already selected.
        private async Task QueueModsForDownloadAsync(List<string> steamIds, CancellationToken ct)
        {
            if (steamIds == null || !steamIds.Any())
            {
                _logger.LogWarning("QueueModsForDownloadAsync called with no Steam IDs.", nameof(ModListIOService));
                return;
            }

            ProgressDialogViewModel progressDialog = null;
            CancellationTokenSource linkedCts = null;
            using var semaphore = new SemaphoreSlim(MaxParallelApiOperations);

            // Use thread-safe collections/counters
            int processedCount = 0;
            int addedCount = 0;
            int alreadyQueuedCount = 0;
            int errorCount = 0;
            var apiErrorMessages = new ConcurrentBag<string>();
            var addedNames = new ConcurrentBag<string>();

            try
            {
                // Show progress dialog
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                       "Queueing Mods for Download",
                       "Starting...",
                       canCancel: true,
                       isIndeterminate: false,
                       cts: null, // Will create linked CTS below
                       closeable: true);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                int totalMods = steamIds.Count;
                await ThreadHelper.RunOnUIThreadAsync(() => // Initial progress update
                {
                    progressDialog.Message = $"Preparing to queue {totalMods} mod(s)...";
                    progressDialog.Progress = 0;
                });


                var tasks = new List<Task>();
                foreach (var steamId in steamIds)
                {
                    combinedToken.ThrowIfCancellationRequested();
                    await semaphore.WaitAsync(combinedToken);

                    tasks.Add(Task.Run(async () =>
                    {
                        string currentSteamId = steamId; // Capture loop variable
                        string modIdentifier = $"Steam ID {currentSteamId}"; // Placeholder name

                        try
                        {
                            combinedToken.ThrowIfCancellationRequested();

                            int currentCount = Interlocked.Increment(ref processedCount);
                            string progressMessage = $"Checking {modIdentifier}... ({currentCount}/{totalMods})";
                            await ThreadHelper.RunOnUIThreadAsync(() =>
                            {
                                if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                                {
                                    progressDialog.Message = progressMessage;
                                    progressDialog.Progress = (int)((double)currentCount / totalMods * 100);
                                }
                            });

                            if (_downloadQueueService.IsInQueue(currentSteamId))
                            {
                                _logger.LogDebug($"Mod {currentSteamId} is already in the download queue.", nameof(ModListIOService));
                                Interlocked.Increment(ref alreadyQueuedCount);
                                return;
                            }

                            combinedToken.ThrowIfCancellationRequested();
                            SteamApiResponse apiResponse = null;
                            try
                            {
                                apiResponse = await _steamApiClient.GetFileDetailsAsync(currentSteamId, combinedToken);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception apiExInner)
                            {
                                var errorMsgInner = $"Network/API error for {modIdentifier}: {apiExInner.Message}";
                                _logger.LogWarning(errorMsgInner, nameof(ModListIOService));
                                apiErrorMessages.Add(errorMsgInner);
                                Interlocked.Increment(ref errorCount);
                                return;
                            }

                            combinedToken.ThrowIfCancellationRequested();

                            if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
                            {
                                var errorMsg = $"No details returned from Steam API for {modIdentifier}.";
                                _logger.LogWarning(errorMsg, nameof(ModListIOService));
                                apiErrorMessages.Add(errorMsg);
                                Interlocked.Increment(ref errorCount);
                                return;
                            }

                            var details = apiResponse.Response.PublishedFileDetails.First();
                            modIdentifier = $"'{details.Title ?? currentSteamId}' ({currentSteamId})"; // Update identifier with real name

                            if (details.Result != 1) // 1 = OK
                            {
                                string errorDescription = SteamApiResultHelper.GetDescription(details.Result);
                                var errorMsg = $"Steam API error for {modIdentifier}: {errorDescription} (Code: {details.Result})";
                                _logger.LogWarning(errorMsg, nameof(ModListIOService));
                                apiErrorMessages.Add(errorMsg);
                                Interlocked.Increment(ref errorCount);
                                return;
                            }

                            // Sanity Check: Double-check if the mod is *actually* available via API, although dictionary said Published=true
                            // The workshop visibility might have changed between dictionary update and now.
                            // `details.Result == 1` already covers most cases (like Removed, FriendsOnly, Private).
                            // We could potentially add a check for `details.visibility` if needed, but Result==1 is usually sufficient.

                            DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
                            DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;

                            var modInfoDto = new ModInfoDto
                            {
                                Name = details.Title ?? $"Unknown Mod {currentSteamId}",
                                SteamId = currentSteamId,
                                Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={currentSteamId}",
                                PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                                StandardDate = apiUpdateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                                FileSize = details.FileSize
                            };

                            if (_downloadQueueService.AddToQueue(modInfoDto))
                            {
                                Interlocked.Increment(ref addedCount);
                                addedNames.Add(modInfoDto.Name);
                                _logger.LogInfo($"Added {modIdentifier} to download queue.", nameof(ModListIOService));
                            }
                            else
                            {
                                var errorMsg = $"Failed to add {modIdentifier} to queue internally.";
                                _logger.LogWarning(errorMsg, nameof(ModListIOService));
                                apiErrorMessages.Add(errorMsg);
                                Interlocked.Increment(ref errorCount);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInfo($"Task cancelled for mod {modIdentifier}.", nameof(ModListIOService));
                        }
                        catch (Exception taskEx)
                        {
                            var errorMsg = $"Unexpected error processing {modIdentifier}: {taskEx.Message}";
                            _logger.LogException(taskEx, errorMsg, nameof(ModListIOService));
                            apiErrorMessages.Add(errorMsg);
                            Interlocked.Increment(ref errorCount);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, combinedToken));
                }

                await Task.WhenAll(tasks);

                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    if (combinedToken.IsCancellationRequested)
                    {
                        _logger.LogInfo("Operation cancelled during or after task execution.", nameof(ModListIOService));
                        progressDialog?.ForceClose();
                        _dialogService.ShowWarning("Operation Cancelled", "Queueing mods for download was cancelled.");
                        return;
                    }

                    progressDialog?.CompleteOperation("Download queueing complete.");

                    int finalAddedCount = addedCount;
                    int finalAlreadyQueuedCount = alreadyQueuedCount;
                    int finalErrorCount = errorCount;
                    List<string> finalErrorMessages = apiErrorMessages.ToList();

                    var sb = new StringBuilder();
                    if (finalAddedCount > 0) sb.AppendLine($"{finalAddedCount} mod(s) added to the download queue.");
                    else sb.AppendLine("No new mods were added to the download queue.");

                    if (finalAlreadyQueuedCount > 0) sb.AppendLine($"{finalAlreadyQueuedCount} selected mod(s) were already in the queue.");
                    if (finalErrorCount > 0)
                    {
                        sb.AppendLine($"{finalErrorCount} selected mod(s) could not be added due to errors:");
                        foreach (var errMsg in finalErrorMessages.Take(5)) sb.AppendLine($"  - {errMsg}");
                        if (finalErrorMessages.Count > 5) sb.AppendLine("    (Check logs for more details...)");
                    }

                    // Show appropriate dialog
                    if (finalErrorCount > 0 && finalAddedCount > 0) _dialogService.ShowWarning("Download Partially Queued", sb.ToString().Trim());
                    else if (finalErrorCount > 0 && finalAddedCount == 0) _dialogService.ShowError("Download Queue Failed", sb.ToString().Trim());
                    else _dialogService.ShowInformation("Download Queued", sb.ToString().Trim());

                    if (finalAddedCount > 0)
                    {
                        _navigationService.RequestTabSwitch("Downloader");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Queueing mods for download was cancelled.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                // Don't show summary dialog
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Outer error during QueueModsForDownloadAsync.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await ThreadHelper.RunOnUIThreadAsync(() => _dialogService.ShowError("Queueing Error", $"An unexpected error occurred: {ex.Message}"));
            }
            finally
            {
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                linkedCts?.Dispose();
                // Semaphore disposed by using statement
            }
        }
        #endregion
    }
}