#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Infrastructure.Workshop.Download.Execution;
using RimSharp.Infrastructure.Workshop.Download.Models;
using RimSharp.Infrastructure.Workshop.Download.Parsing;
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models;
using RimSharp.Infrastructure.Workshop.Download.Processing;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop.Download
{
    /// <summary>
    /// Downloads the specified Workshop items using SteamCMD.
    /// Implements a retry mechanism for failed downloads.
    /// </summary>
    public class SteamCmdDownloader : ISteamCmdDownloader
    {
        private const string BackupSuffix = "_backup";
        private const int MaxDownloadAttempts = 3;
        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _gamePathService;
        private readonly ISteamCmdScriptGenerator _scriptGenerator;
        private readonly ISteamCmdProcessRunner _processRunner;
        private readonly ISteamCmdLogParser _logParser; 
        private readonly IDownloadedItemProcessor _itemProcessor;

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            IDialogService dialogService,
            ILoggerService logger,
            IPathService gamePathService,
            ISteamCmdScriptGenerator scriptGenerator,
            ISteamCmdProcessRunner processRunner,
            ISteamCmdLogParser logParser,
            IDownloadedItemProcessor itemProcessor
            )
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gamePathService = gamePathService ?? throw new ArgumentNullException(nameof(gamePathService));
            _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _logParser = logParser ?? throw new ArgumentNullException(nameof(logParser));
            _itemProcessor = itemProcessor ?? throw new ArgumentNullException(nameof(itemProcessor));
        }

        public async Task<SteamCmdDownloadResult> DownloadModsAsync(
                        IEnumerable<DownloadItem> itemsToDownload,
                        bool validate,
                        IProgress<(int current, int total, string message)>? progress = null,
                        CancellationToken cancellationToken = default)
        {
            _logger.LogInfo($"Starting SteamCMD download operation (Retry Strategy: {MaxDownloadAttempts} attempts, Comprehensive Logging)", "SteamCmdDownloader");
            var result = new SteamCmdDownloadResult();
            string userModsPath = _gamePathService.GetModsPath();
            bool isSetupComplete = await _installer.CheckSetupAsync();
            if (!isSetupComplete || string.IsNullOrEmpty(_pathService.SteamCmdExePath))
            {
                _logger.LogError("Download failed: SteamCMD is not set up properly or executable path is missing.", "SteamCmdDownloader");
                result.LogMessages.Add("Download failed: SteamCMD is not set up.");
                result.OverallSuccess = false;
                return result;
            }
            if (!EnsurePathValidity(userModsPath, result)) return result;

            var validItems = itemsToDownload
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.SteamId) && long.TryParse(i.SteamId.Trim(), out _))
                .OrderBy(i => i.FileSize)
                .ToList();

            _logger.LogDebug($"Sorted item order (IDs): {string.Join(", ", validItems.Select(i => $"{i.SteamId}({i.FileSize}b)"))}", "SteamCmdDownloader");

            if (!validItems.Any())
            {
                _logger.LogInfo("Download skipped: No valid workshop items provided", "SteamCmdDownloader");
                result.LogMessages.Add("Download skipped: No valid workshop items provided.");
                result.OverallSuccess = true; 
                return result;
            }

            if (validItems.Any(i => i.FileSize <= 0))
            {
                _logger.LogWarning("Some items have missing or zero file size, sorting might place them first.", "SteamCmdDownloader");
            }

            var itemLookup = validItems.ToDictionary(i => i.SteamId!);
            var requestedIds = itemLookup.Keys.ToHashSet();
            var finalSessionResults = new Dictionary<string, (bool Success, DateTime Timestamp, string? Reason)>();
            var itemsToAttemptIds = new HashSet<string>(requestedIds);
            string steamCmdInstallDir = _pathService.SteamCmdInstallPath;
            string steamCmdExeDir = Path.GetDirectoryName(_pathService.SteamCmdExePath) ?? steamCmdInstallDir;
            string steamCmdSteamAppsActualPath = _pathService.SteamCmdSteamAppsPath;
            string steamCmdWorkshopContentPath = _pathService.SteamCmdWorkshopContentPath;
            string workshopLogPath = Path.Combine(steamCmdInstallDir, "logs", "workshop_log.txt");
            string contentLogPath = Path.Combine(steamCmdInstallDir, "logs", "content_log.txt"); 
            string bootstrapLogPath = Path.Combine(steamCmdInstallDir, "logs", "bootstrap_log.txt"); 
            _logger.LogDebug($"SteamCMD Working Directory (for execution): {steamCmdExeDir}", "SteamCmdDownloader");
            _logger.LogDebug($"Workshop Log Path: {workshopLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Content Log Path: {contentLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Bootstrap Log Path: {bootstrapLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"SteamCMD Download Target (force_install_dir): {steamCmdSteamAppsActualPath}", "SteamCmdDownloader");
            _logger.LogDebug($"SteamCMD Workshop Content Path (source for processing): {steamCmdWorkshopContentPath}", "SteamCmdDownloader");
            _logger.LogDebug($"User Mods Path (Final Destination): {userModsPath}", "SteamCmdDownloader");

            var finalSucceededItems = new List<DownloadItem>();
            var finalFailedItemsInfo = new List<FailedDownloadInfo>();
            int lastExitCode = -1;
            string lastAttemptPrimaryLogPath = string.Empty;

            try
            {
                await CleanTemporaryLocationAsync(steamCmdWorkshopContentPath, result, cancellationToken);
                string downloadsPath = Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", "downloads");
                await CleanDirectoryContentsAsync(downloadsPath, "Workshop Downloads", result, cancellationToken);
                string workshopTempPath = Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", "temp");
                await CleanDirectoryContentsAsync(workshopTempPath, "Workshop Temp", result, cancellationToken);
                string acfFileName = $"appworkshop_294100.acf";
                string acfPath = Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", acfFileName);
                _logger.LogInfo($"Attempting cleanup of Workshop Manifest file: {acfPath}", "SteamCmdDownloader");
                result.LogMessages.Add($"Cleaning Workshop Manifest file ({acfFileName})...");
                try
                {
                    if (File.Exists(acfPath))
                    {
                        File.Delete(acfPath);
                        await Task.Delay(50, cancellationToken);

                        if (!File.Exists(acfPath))
                        {
                            _logger.LogInfo($"Successfully deleted Workshop Manifest file: {acfPath}", "SteamCmdDownloader");
                            result.LogMessages.Add("Workshop Manifest file deleted.");
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to verify deletion of Workshop Manifest file after attempt: {acfPath}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Warning: Failed to verify deletion of {acfFileName}. It might be locked.");
                        }
                    }
                    else
                    {
                        _logger.LogDebug($"Workshop Manifest file not found, skipping deletion: {acfPath}", "SteamCmdDownloader");
                        result.LogMessages.Add("Workshop Manifest file not found, no cleanup needed.");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to delete Workshop Manifest file '{acfPath}': {ex.Message}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Failed to delete Workshop Manifest file '{acfFileName}'. Error: {ex.Message}");
                }
                cancellationToken.ThrowIfCancellationRequested();
                for (int attempt = 1; attempt <= MaxDownloadAttempts && itemsToAttemptIds.Any(); attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInfo($"--- Starting Download Attempt {attempt} of {MaxDownloadAttempts} for {itemsToAttemptIds.Count} items ---", "SteamCmdDownloader");
                    result.LogMessages.Add($"--- Attempt {attempt}/{MaxDownloadAttempts} for {itemsToAttemptIds.Count} items ---");

                    string scriptId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string scriptPath = Path.Combine(steamCmdExeDir, $"rimsharp_dl_script_{scriptId}.txt");
                    string primaryLogPath = Path.Combine(steamCmdExeDir, $"rimsharp_dl_log_{scriptId}.log");
                    lastAttemptPrimaryLogPath = primaryLogPath;

                    var currentAttemptItems = validItems.Where(i => itemsToAttemptIds.Contains(i.SteamId!)).ToList();
                    if (!currentAttemptItems.Any()) { break; }
                    if (!await GenerateScriptAsync(steamCmdExeDir, scriptId, steamCmdSteamAppsActualPath, currentAttemptItems, validate, result, cancellationToken))
                    {
                        finalFailedItemsInfo.AddRange(currentAttemptItems.Select(item => new FailedDownloadInfo(item, "Failed to generate download script.")));
                        itemsToAttemptIds.Clear();
                        goto EndProcessing;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInfo($"Attempt {attempt}: Executing SteamCMD...", "SteamCmdDownloader");
                    result.LogMessages.Add($"Attempt {attempt}: Executing SteamCMD...");
                    DateTime filterTime = DateTime.Now.AddSeconds(-3);
                    _logger.LogInfo($"Attempt {attempt}: Parsing logs starting from >= {filterTime:O}", "SteamCmdDownloader");

                    try
                    {
                        lastExitCode = await _processRunner.RunSteamCmdAsync(
                            scriptPath,
                            primaryLogPath,
                            steamCmdExeDir,
                            cancellationToken);
                        result.ExitCode = lastExitCode;
                        _logger.LogInfo($"Attempt {attempt}: SteamCMD process exited with code: {lastExitCode}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: SteamCMD exited ({lastExitCode}).");
                    }
                    catch (FileNotFoundException fnfEx)
                    {
                        _logger.LogError($"Attempt {attempt}: FAILED to run SteamCMD process: {fnfEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Error running SteamCMD: {fnfEx.Message}. Aborting download operation.");
                        result.ExitCode = -995;
                        result.OverallSuccess = false;
                        finalFailedItemsInfo.AddRange(currentAttemptItems.Select(item => new FailedDownloadInfo(item, "SteamCMD executable/script not found.")));
                        itemsToAttemptIds.Clear();
                        goto EndProcessing;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception runEx)
                    {
                        _logger.LogError($"Attempt {attempt}: FAILED unexpectedly during SteamCMD execution: {runEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Unexpected error running SteamCMD: {runEx.Message}.");
                        lastExitCode = -994;
                        result.ExitCode = lastExitCode;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInfo($"Attempt {attempt}: Parsing session logs (Workshop, Primary, Content, Bootstrap)...", "SteamCmdDownloader");
                    result.LogMessages.Add($"Attempt {attempt}: Parsing session logs...");

                    var logFilePaths = new SteamCmdLogFilePaths
                    {
                        WorkshopLogPath = workshopLogPath,
                        PrimaryExecutionLogPath = primaryLogPath,
                        ContentLogPath = contentLogPath,
                        BootstrapLogPath = bootstrapLogPath
                    };

                    SteamCmdSessionLogParseResult sessionParseResult = new();
                    try
                    {
                        sessionParseResult = await _logParser.ParseSteamCmdSessionLogsAsync(
                            logFilePaths,
                            itemsToAttemptIds,
                            filterTime,
                            cancellationToken);

                        _logger.LogInfo($"Attempt {attempt}: Log parsing complete. Processed {sessionParseResult.ProcessedWorkshopEntryCount} workshop entries. Status: {sessionParseResult.OverallStatus.Flags}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Log parsing complete ({sessionParseResult.ProcessedWorkshopEntryCount} workshop entries). Status: {sessionParseResult.OverallStatus.Flags}");
                        if (sessionParseResult.CriticalMessages.Any())
                        {
                            result.LogMessages.Add($"--- Critical Messages (Attempt {attempt}) ---");
                            result.LogMessages.AddRange(sessionParseResult.CriticalMessages);
                            result.LogMessages.Add($"--- End Critical Messages ---");
                        }

                        if (attempt == 1 || sessionParseResult.OverallStatus.HasAnyError)
                        {
                            foreach (var kvp in sessionParseResult.LogSamples)
                            {
                                if (kvp.Value.Any())
                                {
                                    result.LogMessages.Add($"=== {kvp.Key.ToUpperInvariant()} LOG SAMPLE (Attempt {attempt}, last {kvp.Value.Count} lines) ===");
                                    result.LogMessages.AddRange(kvp.Value);
                                    result.LogMessages.Add($"=== END OF {kvp.Key.ToUpperInvariant()} LOG SAMPLE ===");
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception parseEx)
                    {
                        _logger.LogError($"Attempt {attempt}: Unexpected error during comprehensive log parsing: {parseEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Unexpected error parsing session logs: {parseEx.Message}");
                    }
                    var nextAttemptFailedIds = new HashSet<string>();
                    int newlySucceededThisAttempt = 0;
                    int stillFailingThisAttempt = 0;

                    if (sessionParseResult.OverallStatus.HasLoginFailed ||
                        sessionParseResult.OverallStatus.HasDiskError ||
                        sessionParseResult.OverallStatus.HasScriptError)
                    {
                        _logger.LogWarning($"Attempt {attempt}: Critical session failure detected ({sessionParseResult.OverallStatus.Flags}). Marking all attempted items as failed for this attempt.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Critical session failure detected ({sessionParseResult.OverallStatus.Flags}). Assuming items failed.");
                        nextAttemptFailedIds.UnionWith(itemsToAttemptIds);
                        stillFailingThisAttempt = itemsToAttemptIds.Count;
                    }
                    else
                    {
                        foreach (string currentItemId in itemsToAttemptIds)
                        {
                            bool isNowConsideredSuccess = false;
                            DateTime latestTimestampForId = DateTime.MinValue;
                            string? failureReasonForId = "Unknown error or no log entry";
                            if (sessionParseResult.WorkshopItemResults.TryGetValue(currentItemId, out var attemptResult))
                            {
                                if (!finalSessionResults.TryGetValue(currentItemId, out var existingFinalStatus) || attemptResult.Timestamp > existingFinalStatus.Timestamp)
                                {
                                    finalSessionResults[currentItemId] = attemptResult;
                                    _logger.LogDebug($"Attempt {attempt}: Updated final status for {currentItemId} based on log at {attemptResult.Timestamp:O}. Success: {attemptResult.Success}");
                                }
                            }

                            if (finalSessionResults.TryGetValue(currentItemId, out var latestOverallStatus))
                            {
                                isNowConsideredSuccess = latestOverallStatus.Success;
                                latestTimestampForId = latestOverallStatus.Timestamp;
                                failureReasonForId = latestOverallStatus.Reason;
                            }

                            if (!isNowConsideredSuccess)
                            {
                                nextAttemptFailedIds.Add(currentItemId);
                                stillFailingThisAttempt++;
                                _logger.LogWarning($"Attempt {attempt}: Item {currentItemId} marked as FAILED (Reason: {failureReasonForId ?? "N/A"}, Last Log: {latestTimestampForId:O})", "SteamCmdDownloader");
                            }
                            else
                            {
                                newlySucceededThisAttempt++;
                                _logger.LogInfo($"Attempt {attempt}: Item {currentItemId} marked as SUCCEEDED (Log at {latestTimestampForId:O})", "SteamCmdDownloader");
                            }
                        }
                    }

                    _logger.LogInfo($"Attempt {attempt} Summary: Succeeded now: {newlySucceededThisAttempt}, Still Failing: {stillFailingThisAttempt}, Items for next attempt: {nextAttemptFailedIds.Count}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Attempt {attempt} Summary: Succeeded now: {newlySucceededThisAttempt}, Still Failing: {stillFailingThisAttempt}.");

                    itemsToAttemptIds = nextAttemptFailedIds;

                    if (itemsToAttemptIds.Any() && attempt < MaxDownloadAttempts)
                    {
                        _logger.LogInfo($"Waiting 1 second before attempt {attempt + 1}...", "SteamCmdDownloader");
                        await Task.Delay(1000, cancellationToken);
                    }
                    CleanupScriptFile(scriptPath);
                }

EndProcessing:
                _logger.LogInfo("Consolidating results after all download attempts...", "SteamCmdDownloader");
                result.LogMessages.Add("Consolidating results after all attempts...");

                var provisionallySucceededIds = new HashSet<string>();
                if (!finalSessionResults.Any() && requestedIds.Any() && !File.Exists(workshopLogPath) && !File.Exists(lastAttemptPrimaryLogPath))
                {
                    _logger.LogError($"Neither Workshop log '{workshopLogPath}' nor last primary log '{lastAttemptPrimaryLogPath}' found or usable. Assuming all {requestedIds.Count} items failed.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Error: Key log files unusable after all attempts. Assuming all items failed.");
                    finalFailedItemsInfo.AddRange(requestedIds.Select(id => new FailedDownloadInfo(itemLookup[id], "Key log files were missing or unreadable.")));
                }
                else
                {
                    foreach (var requestedId in requestedIds)
                    {
                        if (finalSessionResults.TryGetValue(requestedId, out var latestResult) && latestResult.Success)
                        {
                            provisionallySucceededIds.Add(requestedId);
                            _logger.LogInfo($"Item {requestedId} final status from logs: SUCCEEDED (Log entry at {latestResult.Timestamp:O})", "SteamCmdDownloader");
                        }
                        else
                        {
                            string reason = finalSessionResults.TryGetValue(requestedId, out var failResult) ? (failResult.Reason ?? "Unknown/NoLog") : "No success result found in logs";
                            DateTime? failTime = finalSessionResults.TryGetValue(requestedId, out failResult) ? failResult.Timestamp : (DateTime?)null;
                            _logger.LogWarning($"Item {requestedId} final status from logs: FAILED (Reason: {reason}, Last Log At: {failTime?.ToString("O") ?? "N/A"})", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {requestedId} failed download (Reason: {reason})");

                            finalFailedItemsInfo.Add(new FailedDownloadInfo(itemLookup[requestedId], $"Download failed: {reason}"));
                        }
                    }
                }
                await ProcessSucceededItemsAsync(
                    provisionallySucceededIds,
                    itemLookup,
                    steamCmdWorkshopContentPath,
                    userModsPath,
                    result,
                    finalSucceededItems,
                    finalFailedItemsInfo,
                    progress,
                    cancellationToken);
                PopulateFinalResult(result, finalSucceededItems, finalFailedItemsInfo);
                SetOverallSuccessAndLogFinalStatus(result, validItems.Any(), lastExitCode);
            }
            catch (OperationCanceledException)
            {
                HandleCancellation(result, validItems, finalSucceededItems, finalFailedItemsInfo);
            }
            catch (Exception ex)
            {
                HandleUnexpectedException(ex, result, validItems, finalSucceededItems, finalFailedItemsInfo);
            }
            finally
            {
                LogFinalLogFilePaths(lastAttemptPrimaryLogPath, workshopLogPath, contentLogPath, bootstrapLogPath, result);
            }

            return result;
        }

        private bool EnsurePathValidity(string userModsPath, SteamCmdDownloadResult result)
        {
            string? modsParentDir = Path.GetDirectoryName(userModsPath ?? ".");
            if (string.IsNullOrEmpty(userModsPath) || string.IsNullOrEmpty(modsParentDir) || !Directory.Exists(modsParentDir))
            {
                _logger.LogError($"Download failed: RimWorld Mods Path ('{userModsPath}') is not configured or its parent directory does not exist.", "SteamCmdDownloader");
                result.LogMessages.Add($"Download failed: RimWorld Mods Path is not configured or invalid.");
                result.OverallSuccess = false;
                _dialogService.ShowError("Download Failed", "Cannot download mods because the RimWorld Mods Path is not configured or seems invalid (parent directory inaccessible) in the application settings.");
                return false;
            }
            _logger.LogInfo($"RimWorld Mods Path (Target): {userModsPath}", "SteamCmdDownloader");
            return true;
        }

        private async Task CleanTemporaryLocationAsync(string tempPath, SteamCmdDownloadResult result, CancellationToken cancellationToken)
        {
            const int maxRetries = 3;
            const int retryDelayMilliseconds = 250;

            _logger.LogInfo($"Attempting robust cleanup of temporary download location: {tempPath}", "SteamCmdDownloader");
            result.LogMessages.Add($"Cleaning temporary download location (before start, Max Retries: {maxRetries})...");

            if (!Directory.Exists(tempPath))
            {
                _logger.LogInfo($"Temporary download location does not exist, skipping cleanup: {tempPath}", "SteamCmdDownloader");
                result.LogMessages.Add("Temporary download location does not exist, no cleanup needed.");
                try
                {
                    string? parentDir = Path.GetDirectoryName(tempPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                        _logger.LogDebug($"Ensured parent directory exists: {parentDir}", "SteamCmdDownloader");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not ensure parent directory exists for temp location '{tempPath}': {ex.Message}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Could not ensure parent directory exists for temp location.");
                }
                return;
            }

            int cleanupErrors = 0;
            string[] itemsToDelete = Array.Empty<string>();

            try
            {
                itemsToDelete = Directory.GetFileSystemEntries(tempPath);
                _logger.LogDebug($"Found {itemsToDelete.Length} file system item(s) in temp location '{tempPath}' to remove.", "SteamCmdDownloader");
            }
            catch (Exception listEx)
            {
                _logger.LogError($"Failed to list contents of temporary directory '{tempPath}': {listEx.Message}", "SteamCmdDownloader");
                result.LogMessages.Add($"Error: Could not list contents of temporary download location. Cleanup aborted. Check permissions.");
                return;
            }

            if (!itemsToDelete.Any())
            {
                _logger.LogInfo($"Temporary location '{tempPath}' is already empty.", "SteamCmdDownloader");
                result.LogMessages.Add("Temporary download location is already empty.");
                return;
            }

            foreach (string itemPath in itemsToDelete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string itemName = Path.GetFileName(itemPath);
                bool isDirectory = Directory.Exists(itemPath);

                _logger.LogDebug($"Attempting to delete {(isDirectory ? "directory" : "file")}: '{itemName}'", "SteamCmdDownloader");

                bool deletedSuccessfully = false;
                Exception? lastException = null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (isDirectory)
                        {
                            if (Directory.Exists(itemPath))
                            {
                                Directory.Delete(itemPath, true);
                                _logger.LogDebug($"Successfully deleted directory '{itemName}' on attempt {attempt}.", "SteamCmdDownloader");
                            }
                            else
                            {
                                _logger.LogDebug($"Directory '{itemName}' disappeared before delete attempt {attempt}. Skipping.", "SteamCmdDownloader");
                            }
                        }
                        else
                        {
                            if (File.Exists(itemPath))
                            {
                                File.Delete(itemPath);
                                _logger.LogDebug($"Successfully deleted file '{itemName}' on attempt {attempt}.", "SteamCmdDownloader");
                            }
                            else
                            {
                                _logger.LogDebug($"File '{itemName}' disappeared before delete attempt {attempt}. Skipping.", "SteamCmdDownloader");
                            }
                        }
                        deletedSuccessfully = true;
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (IOException ioEx)
                    {
                        lastException = ioEx;
                        _logger.LogDebug($"Attempt {attempt}/{maxRetries} failed to delete '{itemName}': {ioEx.GetType().Name} - {ioEx.Message}", "SteamCmdDownloader");
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        lastException = uaEx;
                        _logger.LogWarning($"Attempt {attempt}/{maxRetries} failed to delete '{itemName}' due to permissions: {uaEx.Message}", "SteamCmdDownloader");
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _logger.LogError($"Attempt {attempt}/{maxRetries} failed to delete '{itemName}' with unexpected error: {ex.GetType().Name} - {ex.Message}", "SteamCmdDownloader");
                    }

                    if (!deletedSuccessfully && attempt < maxRetries)
                    {
                        _logger.LogDebug($"Waiting {retryDelayMilliseconds}ms before retry {attempt + 1} for '{itemName}'.", "SteamCmdDownloader");
                        await Task.Delay(retryDelayMilliseconds, cancellationToken);
                    }
                }

                if (!deletedSuccessfully)
                {
                    cleanupErrors++;
                    string errorType = lastException?.GetType().Name ?? "Unknown Error";
                    string errorMessage = lastException?.Message ?? "No specific error message.";
                    _logger.LogError($"Failed to delete '{itemName}' after {maxRetries} attempts. Last error: {errorType} - {errorMessage}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Failed to clean up '{itemName}' after {maxRetries} attempts ({errorType}). Check logs/permissions.");
                }
            }
            if (cleanupErrors == 0)
            {
                _logger.LogInfo($"Successfully cleaned {itemsToDelete.Length} item(s) from '{tempPath}'.", "SteamCmdDownloader");
                result.LogMessages.Add("Temporary download location cleaned successfully.");
            }
            else
            {
                _logger.LogWarning($"Finished cleaning temporary location '{tempPath}' with {cleanupErrors} error(s). Some items may remain.", "SteamCmdDownloader");
                result.LogMessages.Add($"Finished cleaning temporary location with {cleanupErrors} error(s).");
            }

            try
            {
                string[] remainingItems = Directory.GetFileSystemEntries(tempPath);
                if (remainingItems.Length > 0)
                {
                    _logger.LogWarning($"Cleanup verification FAILED. {remainingItems.Length} item(s) still exist in '{tempPath}'. This could be due to persistent locks or permission issues.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Verification check found {remainingItems.Length} item(s) remaining in the temporary download folder after cleanup attempt. Check logs.");
                }
                else
                {
                    _logger.LogInfo($"Cleanup verification PASSED. Temporary location '{tempPath}' is empty.", "SteamCmdDownloader");
                }
            }
            catch (Exception verifyEx)
            {
                _logger.LogWarning($"Error during post-cleanup verification of '{tempPath}': {verifyEx.Message}", "SteamCmdDownloader");
                result.LogMessages.Add($"Warning: Error verifying temporary location cleanup: {verifyEx.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private async Task<bool> GenerateScriptAsync(string scriptDir, string scriptId, string forceInstallDir, List<DownloadItem> items, bool validate, SteamCmdDownloadResult result, CancellationToken cancellationToken)
        {
            _logger.LogInfo($"Generating SteamCMD script for {items.Count} items...", "SteamCmdDownloader");
            result.LogMessages.Add($"Generating script...");
            try
            {
                await _scriptGenerator.GenerateScriptAsync(
                    scriptDir, scriptId, forceInstallDir, items, validate, cancellationToken);
                _logger.LogInfo($"Script generated: {Path.Combine(scriptDir, $"rimsharp_dl_script_{scriptId}.txt")}", "SteamCmdDownloader");
                result.LogMessages.Add($"Script generated.");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception genEx)
            {
                _logger.LogError($"FAILED to generate script: {genEx.Message}", "SteamCmdDownloader");
                result.LogMessages.Add($"Error generating script: {genEx.Message}. Aborting.");
                result.ExitCode = -996;
                result.OverallSuccess = false;
                return false;
            }
        }

        private void CleanupScriptFile(string scriptPath)
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); _logger.LogDebug($"Cleaned up script file: {scriptPath}", "SteamCmdDownloader"); }
                catch (Exception ex) { _logger.LogWarning($"Failed to clean up script file '{scriptPath}': {ex.Message}", "SteamCmdDownloader"); }
            }
        }

        private async Task CleanDirectoryContentsAsync(string directoryPath, string description, SteamCmdDownloadResult result, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogDebug($"Cleanup skipped: '{description}' directory not found at '{directoryPath}'", "SteamCmdDownloader");
                return;
            }

            await Task.Run(() =>
            {
                if (!Directory.Exists(directoryPath))
                {
                    _logger.LogDebug($"Cleanup skipped: '{description}' directory not found at '{directoryPath}'", "SteamCmdDownloader");
                    return;
                }

                _logger.LogInfo($"Attempting cleanup of '{description}' directory contents: {directoryPath}", "SteamCmdDownloader");
                result.LogMessages.Add($"Cleaning {description} directory...");
                int errors = 0;
                try
                {
                    var dirInfo = new DirectoryInfo(directoryPath);
                    foreach (FileInfo file in dirInfo.GetFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { file.Delete(); }
                        catch (Exception ex) { _logger.LogWarning($"Failed to delete file during cleanup: {file.FullName} - {ex.Message}", "SteamCmdDownloader"); errors++; }
                    }
                    foreach (DirectoryInfo subDir in dirInfo.GetDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { subDir.Delete(true); }
                        catch (Exception ex) { _logger.LogWarning($"Failed to delete directory during cleanup: {subDir.FullName} - {ex.Message}", "SteamCmdDownloader"); errors++; }
                    }
                    _logger.LogInfo($"Cleanup of '{description}' contents finished with {errors} errors.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Cleanup of {description} finished ({errors} errors).");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"Error during cleanup of '{description}' directory '{directoryPath}': {ex.Message}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Error cleaning up {description}: {ex.Message}");
                }
            }, cancellationToken);
        }

        private async Task ProcessSucceededItemsAsync(
            IEnumerable<string> successIds,
            Dictionary<string, DownloadItem> itemLookup,
            string sourceBasePath,
            string targetBasePath,
            SteamCmdDownloadResult result,
            List<DownloadItem> finalSucceededItems,
            List<FailedDownloadInfo> finalFailedItems,
            IProgress<(int current, int total, string message)>? progress,
            CancellationToken cancellationToken)
        {
            var successIdsList = successIds.ToList();
            int total = successIdsList.Count;
            int current = 0;

            _logger.LogInfo($"Processing {total} items marked as successful by logs...", "SteamCmdDownloader");
            result.LogMessages.Add($"Processing {total} successfully downloaded items...");

            foreach (var successId in successIdsList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadItem currentItem = itemLookup[successId];
                current++;

                progress?.Report((current, total, $"Processing mod: {currentItem.Name}"));

                string sourceDownloadPath = Path.Combine(sourceBasePath, successId);
                string targetModPath = Path.Combine(targetBasePath, successId);

                _logger.LogInfo($"Handing off item {successId} ('{currentItem.Name}') to Item Processor.", "SteamCmdDownloader");
                result.LogMessages.Add($"--- Processing Item {successId} ({currentItem.Name}) ---");
                (bool processedOk, string reason) processedResult = (false, "Processing did not start.");
                try
                {
                    processedResult = await _itemProcessor.ProcessItemAsync(
                        currentItem,
                        sourceDownloadPath,
                        targetModPath,
                        BackupSuffix,
                        cancellationToken);

                    var processorLogs = _itemProcessor.GetLogMessages();
                    if (processorLogs != null)
                    {
                        result.LogMessages.AddRange(processorLogs);
                    }
                    result.LogMessages.Add($"--- Finished Processing Item {successId} ---");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception procEx)
                {
                    _logger.LogError($"Item {successId}: Unexpected error during item processing step: {procEx.Message}", "SteamCmdDownloader");
                    result.LogMessages.Add($"CRITICAL ERROR processing item {successId} ({currentItem.Name}): {procEx.Message}");
                    processedResult = (false, procEx.Message);

                    var logs = _itemProcessor.GetLogMessages();
                    if (logs != null)
                    {
                        result.LogMessages.AddRange(logs);
                    }
                    result.LogMessages.Add($"--- Finished Processing Item {successId} (with error) ---");
                }

                if (processedResult.processedOk)
                {
                    finalSucceededItems.Add(currentItem);
                    _logger.LogInfo($"Item {successId} successfully processed by Item Processor.", "SteamCmdDownloader");
                }
                else
                {
                    if (!finalFailedItems.Any(fi => fi.Item != null && fi.Item.SteamId == currentItem.SteamId))
                    {
                        finalFailedItems.Add(new FailedDownloadInfo(currentItem, processedResult.reason));
                    }
                    _logger.LogWarning($"Item {successId} processing FAILED by Item Processor. Reason: {processedResult.reason}", "SteamCmdDownloader");
                }
            }
        }

        private void PopulateFinalResult(SteamCmdDownloadResult result, List<DownloadItem> finalSucceededItems, List<FailedDownloadInfo> finalFailedItems)
        {
            result.SucceededItems.AddRange(finalSucceededItems);

            var distinctFailedItems = finalFailedItems
                                        .Where(failedInfo => failedInfo?.Item != null && !result.SucceededItems.Any(succeededItem => succeededItem.SteamId == failedInfo.Item.SteamId))
                                        .GroupBy(failedInfo => failedInfo.Item.SteamId)
                                        .Select(group => group.First())
                                        .ToList();
            result.FailedItems.Clear();
            result.FailedItems.AddRange(distinctFailedItems);

            _logger.LogInfo($"Final results: Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
            if (result.SucceededItems.Any()) _logger.LogDebug($"Succeeded IDs: {string.Join(", ", result.SucceededItems.Select(i => i.SteamId))}", "SteamCmdDownloader");
            if (result.FailedItems.Any()) _logger.LogWarning($"Failed IDs & Reasons: {string.Join("; ", result.FailedItems.Select(i => $"{i.Item.SteamId}: {i.Reason}"))}", "SteamCmdDownloader");
        }

        private void SetOverallSuccessAndLogFinalStatus(SteamCmdDownloadResult result, bool hadValidItems, int lastExitCode)
        {
            result.OverallSuccess = !result.FailedItems.Any() && (result.SucceededItems.Any() || !hadValidItems);

            if (result.OverallSuccess && hadValidItems)
            {
                _logger.LogInfo("Download operation completed successfully for all requested items.", "SteamCmdDownloader");
                result.LogMessages.Add("Download completed successfully for all items.");
            }
            else if (result.OverallSuccess && !hadValidItems)
            {
            }
            else if (!result.OverallSuccess && result.FailedItems.Any())
            {
                _logger.LogError($"Download operation finished with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}. Last SteamCMD Exit Code: {lastExitCode}.", "SteamCmdDownloader");
                result.LogMessages.Add($"Download completed with {result.FailedItems.Count} failure(s). Last SteamCMD Exit Code: {lastExitCode}. Check messages/logs for details.");
            }
            else 
            {
                _logger.LogWarning($"Download operation finished with unclear status. OverallSuccess={result.OverallSuccess}, Succeeded={result.SucceededItems.Count}, Failed={result.FailedItems.Count}, LastExitCode={lastExitCode}", "SteamCmdDownloader");
                result.LogMessages.Add($"Download finished with unclear status.");
                result.OverallSuccess = false; 
            }

            _logger.LogInfo($"Download processing finished. Overall success flag: {result.OverallSuccess}", "SteamCmdDownloader");
        }

        private void HandleCancellation(SteamCmdDownloadResult result, List<DownloadItem> validItems, List<DownloadItem> finalSucceededItems, List<FailedDownloadInfo> finalFailedItems)
        {
            _logger.LogWarning("Download operation cancelled by user", "SteamCmdDownloader");
            result.LogMessages.Add("Download operation cancelled.");
            result.OverallSuccess = false;
            var succeededSoFarIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet();
            var newlyFailed = validItems
                .Where(i => i?.SteamId != null && !succeededSoFarIds.Contains(i.SteamId))
                .Select(item => new FailedDownloadInfo(item, "Operation cancelled by user."));

            finalFailedItems.AddRange(newlyFailed);

            result.SucceededItems.Clear();
            result.SucceededItems.AddRange(finalSucceededItems);
            var distinctFailed = finalFailedItems.Where(i => i?.Item != null).DistinctBy(i => i.Item.SteamId).ToList();
            result.FailedItems.Clear();
            result.FailedItems.AddRange(distinctFailed);
        }

        private void HandleUnexpectedException(Exception ex, SteamCmdDownloadResult result, List<DownloadItem> validItems, List<DownloadItem> finalSucceededItems, List<FailedDownloadInfo> finalFailedItems)
        {
            _logger.LogError($"Download failed with unexpected exception: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
            result.LogMessages.Add($"Download failed with unexpected exception: {ex.Message}");
            result.OverallSuccess = false;
            var succeededIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet();
            var newlyFailed = validItems
                .Where(i => i?.SteamId != null && !succeededIds.Contains(i.SteamId))
                .Select(item => new FailedDownloadInfo(item, $"An unexpected error occurred: {ex.Message}"));

            finalFailedItems.AddRange(newlyFailed);

            result.SucceededItems.Clear();
            result.SucceededItems.AddRange(finalSucceededItems);
            var distinctFailed = finalFailedItems.Where(i => i?.Item != null).DistinctBy(i => i.Item.SteamId).ToList();
            result.FailedItems.Clear();
            result.FailedItems.AddRange(distinctFailed);
            _dialogService.ShowError("Download Failed", $"An unexpected error occurred during the download process:\n\n{ex.Message}");
        }

        private void LogFinalLogFilePaths(string lastPrimaryLog, string workshopLog, string contentLog, string bootstrapLog, SteamCmdDownloadResult result)
        {
            if (!string.IsNullOrEmpty(lastPrimaryLog) && File.Exists(lastPrimaryLog))
            {
                _logger.LogInfo($"Primary log for last attempt kept at: {lastPrimaryLog}", "SteamCmdDownloader");
                result.LogMessages.Add($"Primary log for last attempt: {lastPrimaryLog}");
            }
            if (File.Exists(workshopLog))
            {
                _logger.LogInfo($"Workshop log file kept at: {workshopLog}", "SteamCmdDownloader");
                result.LogMessages.Add($"Workshop log file: {workshopLog}");
            }
            if (File.Exists(contentLog))
            {
                _logger.LogInfo($"Content log file kept at: {contentLog}", "SteamCmdDownloader");
                result.LogMessages.Add($"Content log file: {contentLog}");
            }
            if (File.Exists(bootstrapLog))
            {
                _logger.LogInfo($"Bootstrap log file kept at: {bootstrapLog}", "SteamCmdDownloader");
                result.LogMessages.Add($"Bootstrap log file: {bootstrapLog}");
            }
        }
    }
}
