#nullable enable
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
        private readonly ISteamWorkshopQueueProcessor _steamWorkshopQueueProcessor;
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
            ILoggerService logger,
            ISteamWorkshopQueueProcessor steamWorkshopQueueProcessor)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _modListManager = modListManager ?? throw new ArgumentNullException(nameof(modListManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService)); // Added
            _modDictionaryService = modDictionaryService ?? throw new ArgumentNullException(nameof(modDictionaryService));
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _steamWorkshopQueueProcessor = steamWorkshopQueueProcessor ?? throw new ArgumentNullException(nameof(steamWorkshopQueueProcessor));

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
                    .Select(m => m.PackageId!.ToLowerInvariant()) // Use null-forgiving operator as we've already checked
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missingModIds = activeModIds
                    .Where(id => !availableModIds.Contains(id))
                    .ToList();

                // Only pass available mod IDs to the manager
                var availableActiveModIds = activeModIds
                    .Where(id => availableModIds.Contains(id))
                    .ToList();

                await Task.Run(() => _modListManager.Initialize(allMods, availableActiveModIds));

                // FIX: Await the async method to ensure the import process doesn't end prematurely.
                await DisplayImportResultsAsync(Path.GetFileName(filePath), availableActiveModIds.Count, missingModIds);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Import Error", $"An unexpected error occurred during import: {ex.Message}");
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

                _dialogService.ShowInformation("Export Successful", $"Mod list exported successfully to {Path.GetFileName(filePath)}!");
            }
            catch (UnauthorizedAccessException)
            {
                _dialogService.ShowError("Export Error", "Error: Permission denied when saving the file.");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Export Error", $"An unexpected error occurred during export: {ex.Message}");
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
        
        // FIX: Changed return type to Task<string?> to indicate the result can be null.
        private Task<string?> ShowFileDialogAsync(string initialDirectory, FileDialogType dialogType)
        {
            return Task.Run(() =>
            {
                // FIX: Declared result as string? to match the return type.
                string? result = null;

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

        // FIX: Changed return type to Task<List<string>?> to indicate the result can be null.
        private async Task<List<string>?> ParseModListFileAsync(string filePath)
        {
            XDocument doc;
            try
            {
                doc = await Task.Run(() => XDocument.Load(filePath));
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Import Error", $"Error loading XML file: {ex.Message}");
                return null;
            }
            
            // FIX: Use the null-conditional operator (?.) for safe navigation.
            var activeModsElement = doc.Root?.Element("activeMods");
            if (activeModsElement is null)
            {
                _dialogService.ShowError("Invalid File Format", "The selected file does not contain a valid mod list format.");
                return null;
            }

            var activeModIds = activeModsElement.Elements("li")
                .Select(e => e.Value.ToLowerInvariant())
                .ToList();

            if (!activeModIds.Any())
            {
                _dialogService.ShowWarning("Import Warning", "The file contains an empty mod list.");
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

            // FIX: Safely get the element and throw if it's somehow null.
            var activeModsElement = doc.Root?.Element("activeMods") ?? throw new InvalidOperationException("Could not find activeMods element in the new XDocument.");

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

            var confirmResult = _dialogService.ShowConfirmation(
                "Import Partially Successful",
                messageBuilder.ToString(),
                showCancel: true); 

            if (confirmResult == MessageDialogResult.OK || confirmResult == MessageDialogResult.Yes)
            {
                _logger.LogInfo($"User chose to download {missingModIds.Count} missing mods from import.", nameof(ModListIOService));
                await HandleMissingModDownloadRequestAsync(missingModIds);
            }
            else
            {
                _logger.LogInfo("User declined to download missing mods from import.", nameof(ModListIOService));
                _dialogService.ShowInformation(
                    "Import Successful",
                    $"Successfully imported mod list from {fileName} with {importedCount} active mods.");
            }
        }
        
        private async Task HandleMissingModDownloadRequestAsync(List<string> missingModIds)
        {
            if (missingModIds == null || !missingModIds.Any()) return;

            List<MissingModGroupViewModel> groups = new List<MissingModGroupViewModel>();
            List<string> unknownIds = new List<string>();
            // FIX: Declare as nullable, as the service method might return null.
            Dictionary<string, ModDictionaryEntry>? allEntries = null;

            ProgressDialogViewModel? prepProgressDialog = null;
            CancellationTokenSource? prepCts = null;

            try
            {
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    prepCts = new CancellationTokenSource(); 
                    prepProgressDialog = _dialogService.ShowProgressDialog(
                        "Preparing Download Options",
                        "Loading mod dictionary...",
                        canCancel: true,
                        isIndeterminate: true,
                        cts: prepCts,
                        closeable: false);
                });
                if (prepProgressDialog == null) throw new InvalidOperationException("Preparation progress dialog not created");
                if (prepCts == null) throw new InvalidOperationException("Preparation CTS not created");


                await Task.Run(() =>
                {
                    allEntries = _modDictionaryService.GetAllEntries();
                }, prepCts.Token);

                prepCts.Token.ThrowIfCancellationRequested(); 

                // FIX: Add a null check before trying to use allEntries.
                if (allEntries is null)
                {
                    _logger.LogWarning("Mod dictionary was null, cannot match missing mods.", nameof(ModListIOService));
                    _dialogService.ShowWarning("Dictionary Missing", "Could not load the mod dictionary to find missing mods.");
                    return; // Exit early
                }
                
                await ThreadHelper.RunOnUIThreadAsync(() => prepProgressDialog.Message = "Matching missing mods...");

                await Task.Run(() =>
                {
                    var allEntriesList = allEntries.Values.ToList();
                    foreach (var missingId in missingModIds.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        prepCts.Token.ThrowIfCancellationRequested();
                        var normalizedMissingId = missingId.ToLowerInvariant();
                        var matchingEntries = allEntriesList
                           .Where(entry => !string.IsNullOrEmpty(entry.PackageId) &&
                                          entry.PackageId.Equals(normalizedMissingId, StringComparison.OrdinalIgnoreCase))
                           .ToList();

                        if (matchingEntries.Any())
                        {
                            var group = new MissingModGroupViewModel(missingId); 
                            foreach (var entry in matchingEntries.OrderBy(e => e.Name)) 
                            {
                                group.Variants.Add(new MissingModVariantViewModel(entry));
                            }
                            group.SelectedVariant = group.Variants.FirstOrDefault(v => v.IsPublished);
                            groups.Add(group);
                        }
                        else
                        {
                            unknownIds.Add(missingId);
                            _logger.LogDebug($"Missing mod ID '{missingId}' not found in dictionary.", nameof(ModListIOService));
                        }
                    }
                }, prepCts.Token);

                prepCts.Token.ThrowIfCancellationRequested();

                await ThreadHelper.RunOnUIThreadAsync(() => prepProgressDialog?.CompleteOperation("Ready."));

                MissingModSelectionDialogOutput? selectionResult = null;
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    var selectionViewModel = new MissingModSelectionDialogViewModel(groups, unknownIds);
                    selectionResult = _dialogService.ShowMissingModSelectionDialog(selectionViewModel);
                });

                // FIX: Add a null check on selectionResult before accessing its properties.
                if (selectionResult != null && selectionResult.Result == MissingModSelectionResult.Download && selectionResult.SelectedSteamIds.Any())
                {
                    _logger.LogInfo($"User selected {selectionResult.SelectedSteamIds.Count} published mod variants to download.", nameof(ModListIOService));
                    await ProcessAndQueueSelectedModsAsync(selectionResult.SelectedSteamIds, CancellationToken.None);
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
                    prepProgressDialog?.ForceClose();
                    _dialogService.ShowWarning("Operation Cancelled", "Preparing download options was cancelled.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Error preparing missing mod download options.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    prepProgressDialog?.ForceClose();
                    _dialogService.ShowError("Error", $"Failed to prepare download options: {ex.Message}");
                });
            }
            finally
            {
                await ThreadHelper.RunOnUIThreadAsync(() => prepProgressDialog?.ForceClose());
                prepCts?.Dispose();
            }
        }

        private async Task ProcessAndQueueSelectedModsAsync(List<string> steamIds, CancellationToken ct)
        {
            if (steamIds == null || !steamIds.Any())
            {
                _logger.LogWarning("ProcessAndQueueSelectedModsAsync called with no Steam IDs.", nameof(ModListIOService));
                return;
            }

            ProgressDialogViewModel? progressDialog = null;
            CancellationTokenSource? linkedCts = null;
            QueueProcessResult queueResult = new QueueProcessResult();

            try
            {
                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    progressDialog = _dialogService.ShowProgressDialog(
                       "Queueing Missing Mods",
                       "Starting...",
                       canCancel: true,
                       isIndeterminate: false,
                       cts: null,
                       closeable: true);
                });
                if (progressDialog == null) throw new InvalidOperationException("Progress dialog view model was not created.");

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, progressDialog.CancellationToken);
                var combinedToken = linkedCts.Token;

                var progressReporter = new Progress<QueueProcessProgress>(update =>
                {
                    ThreadHelper.EnsureUiThread(() => 
                    {
                        if (progressDialog != null && !progressDialog.CancellationToken.IsCancellationRequested)
                        {
                            progressDialog.Message = $"{update.Message} ({update.CurrentItem}/{update.TotalItems})";
                            progressDialog.Progress = (int)((double)update.CurrentItem / update.TotalItems * 100);
                        }
                    });
                });

                queueResult = await _steamWorkshopQueueProcessor.ProcessAndEnqueueModsAsync(steamIds, progressReporter, combinedToken);

                await ThreadHelper.RunOnUIThreadAsync(() =>
                {
                    if (queueResult.WasCancelled)
                    {
                        _logger.LogInfo("Queueing missing mods was cancelled.", nameof(ModListIOService));
                        progressDialog?.ForceClose();
                        _dialogService.ShowWarning("Operation Cancelled", "Queueing selected mods for download was cancelled.");
                        return;
                    }

                    progressDialog?.CompleteOperation("Download queueing complete.");

                    var sb = new StringBuilder();
                    if (queueResult.SuccessfullyAdded > 0) sb.AppendLine($"{queueResult.SuccessfullyAdded} mod(s) added to the download queue.");
                    else sb.AppendLine("No new mods were added to the download queue.");

                    if (queueResult.AlreadyQueued > 0) sb.AppendLine($"{queueResult.AlreadyQueued} selected mod(s) were already in the queue.");
                    if (queueResult.FailedProcessing > 0)
                    {
                        sb.AppendLine($"{queueResult.FailedProcessing} selected mod(s) could not be added due to errors:");
                        foreach (var errMsg in queueResult.ErrorMessages.Take(5)) sb.AppendLine($"  - {errMsg}");
                        if (queueResult.ErrorMessages.Count > 5) sb.AppendLine("    (Check logs for more details...)");
                    }

                    if (queueResult.FailedProcessing > 0 && queueResult.SuccessfullyAdded > 0) _dialogService.ShowWarning("Download Partially Queued", sb.ToString().Trim());
                    else if (queueResult.FailedProcessing > 0 && queueResult.SuccessfullyAdded == 0) _dialogService.ShowError("Download Queue Failed", sb.ToString().Trim());
                    else _dialogService.ShowInformation("Download Queued", sb.ToString().Trim());

                    if (queueResult.SuccessfullyAdded > 0)
                    {
                        _navigationService.RequestTabSwitch("Downloader");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Queueing mods for download was cancelled.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose());
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Outer error during ProcessAndQueueSelectedModsAsync.", nameof(ModListIOService));
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                await ThreadHelper.RunOnUIThreadAsync(() => _dialogService.ShowError("Queueing Error", $"An unexpected error occurred: {ex.Message}"));
            }
            finally
            {
                await ThreadHelper.RunOnUIThreadAsync(() => progressDialog?.ForceClose());
                linkedCts?.Dispose();
            }
        }

        #endregion
    }
}