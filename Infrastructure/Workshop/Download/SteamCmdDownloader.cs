#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; // For DownloadItem
using RimSharp.Infrastructure.Workshop.Core;        // For ISteamCmdPathService, ISteamCmdInstaller
using RimSharp.Infrastructure.Workshop.Download.Execution; // For ISteamCmdScriptGenerator, ISteamCmdProcessRunner
using RimSharp.Infrastructure.Workshop.Download.Models;    // For SteamCmdDownloadResult
using RimSharp.Infrastructure.Workshop.Download.Parsing;     // For ISteamCmdLogParser (NEW)
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models; // For SteamCmdSessionLogParseResult, SteamCmdLogFilePaths (NEW)
using RimSharp.Infrastructure.Workshop.Download.Processing;  // For IDownloadedItemProcessor
using RimSharp.Shared.Services.Contracts;               // For ILoggerService, IDialogService, IPathService

namespace RimSharp.Infrastructure.Workshop.Download
{
    /// <summary>
    /// Orchestrates the download of Steam Workshop mods using SteamCMD, coordinating
    /// script generation, process execution, log parsing, and post-download processing.
    /// Implements a retry mechanism for failed downloads.
    /// </summary>
    public class SteamCmdDownloader : ISteamCmdDownloader
    {
        // --- Constants ---
        private const string BackupSuffix = "_backup"; // Suffix for backup folders during update
        private const int MaxDownloadAttempts = 3;     // Number of times to retry failed downloads
        // --- End Constants ---

        // --- Dependencies ---
        private readonly ISteamCmdPathService _pathService; // From Core
        private readonly ISteamCmdInstaller _installer;     // From Core
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _gamePathService; // RimWorld game path service
        private readonly ISteamCmdScriptGenerator _scriptGenerator; // From Download.Execution
        private readonly ISteamCmdProcessRunner _processRunner;     // From Download.Execution
        private readonly ISteamCmdLogParser _logParser;         // From Download.Parsing (NEW TYPE)
        private readonly IDownloadedItemProcessor _itemProcessor;   // From Download.Processing
        // --- End Dependencies ---

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,       // Core
            ISteamCmdInstaller installer,         // Core
            IDialogService dialogService,
            ILoggerService logger,
            IPathService gamePathService,        // Game paths (for target dir)
            ISteamCmdScriptGenerator scriptGenerator, // New
            ISteamCmdProcessRunner processRunner,     // New
            ISteamCmdLogParser logParser,             // Changed from IWorkshopLogParser
            IDownloadedItemProcessor itemProcessor    // New
            )
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gamePathService = gamePathService ?? throw new ArgumentNullException(nameof(gamePathService));
            _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _logParser = logParser ?? throw new ArgumentNullException(nameof(logParser)); // Changed variable name
            _itemProcessor = itemProcessor ?? throw new ArgumentNullException(nameof(itemProcessor));
        }

        /// <summary>
        /// Downloads the specified Workshop items using SteamCMD.
        /// </summary>
        /// <param name="itemsToDownload">The collection of items to download.</param>
        /// <param name="validate">Whether to include the 'validate' parameter in the SteamCMD command.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="SteamCmdDownloadResult"/> indicating the outcome.</returns>
        public async Task<SteamCmdDownloadResult> DownloadModsAsync(
                        IEnumerable<DownloadItem> itemsToDownload,
                        bool validate,
                        CancellationToken cancellationToken = default)
        {
            _logger.LogInfo($"Starting SteamCMD download operation (Retry Strategy: {MaxDownloadAttempts} attempts, Comprehensive Logging)", "SteamCmdDownloader");
            var result = new SteamCmdDownloadResult();
            string userModsPath = _gamePathService.GetModsPath();

            // --- Initial Checks ---
            bool isSetupComplete = await _installer.CheckSetupAsync();
            if (!isSetupComplete || string.IsNullOrEmpty(_pathService.SteamCmdExePath))
            {
                _logger.LogError("Download failed: SteamCMD is not set up properly or executable path is missing.", "SteamCmdDownloader");
                result.LogMessages.Add("Download failed: SteamCMD is not set up.");
                result.OverallSuccess = false;
                return result;
            }
            if (!await EnsurePathValidityAsync(userModsPath, result)) return result;

            // Filter out invalid items
            var validItems = itemsToDownload
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.SteamId) && long.TryParse(i.SteamId.Trim(), out _))
                .OrderBy(i => i.FileSize)
                .ToList();

            _logger.LogDebug($"Sorted item order (IDs): {string.Join(", ", validItems.Select(i => $"{i.SteamId}({i.FileSize}b)"))}", "SteamCmdDownloader");


            if (!validItems.Any())
            {
                _logger.LogInfo("Download skipped: No valid workshop items provided", "SteamCmdDownloader");
                result.LogMessages.Add("Download skipped: No valid workshop items provided.");
                result.OverallSuccess = true; // No work needed is considered success
                return result;
            }

            if (validItems.Any(i => i.FileSize <= 0))
            {
                _logger.LogWarning("Some items have missing or zero file size, sorting might place them first.", "SteamCmdDownloader");
            }



            // Prepare lookups and state for retries
            var itemLookup = validItems.ToDictionary(i => i.SteamId!);
            var requestedIds = itemLookup.Keys.ToHashSet();
            var finalSessionResults = new Dictionary<string, (bool Success, DateTime Timestamp, string? Reason)>(); // Tracks latest log status per ID across attempts
            var itemsToAttemptIds = new HashSet<string>(requestedIds); // IDs remaining to be downloaded/retried

            // --- Path Setup for SteamCMD operations ---
            string steamCmdInstallDir = _pathService.SteamCmdInstallPath; // Base install dir (e.g., /path/to/steamcmd)
            string steamCmdExeDir = Path.GetDirectoryName(_pathService.SteamCmdExePath) ?? steamCmdInstallDir; // Directory containing steamcmd.exe/sh
            string steamCmdSteamAppsActualPath = _pathService.SteamCmdSteamAppsPath; // Target for 'force_install_dir' (e.g., /path/to/steam)
            string steamCmdWorkshopContentPath = _pathService.SteamCmdWorkshopContentPath; // Where items are actually downloaded (e.g., /path/to/steam/steamapps/workshop/content/294100)
            string workshopLogPath = Path.Combine(steamCmdInstallDir, "logs", "workshop_log.txt");
            string contentLogPath = Path.Combine(steamCmdInstallDir, "logs", "content_log.txt"); // NEW
            string bootstrapLogPath = Path.Combine(steamCmdInstallDir, "logs", "bootstrap_log.txt"); // NEW
            // --- End Path Setup ---

            _logger.LogDebug($"SteamCMD Working Directory (for execution): {steamCmdExeDir}", "SteamCmdDownloader");
            _logger.LogDebug($"Workshop Log Path: {workshopLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Content Log Path: {contentLogPath}", "SteamCmdDownloader"); // Log new path
            _logger.LogDebug($"Bootstrap Log Path: {bootstrapLogPath}", "SteamCmdDownloader"); // Log new path
            _logger.LogDebug($"SteamCMD Download Target (force_install_dir): {steamCmdSteamAppsActualPath}", "SteamCmdDownloader");
            _logger.LogDebug($"SteamCMD Workshop Content Path (source for processing): {steamCmdWorkshopContentPath}", "SteamCmdDownloader");
            _logger.LogDebug($"User Mods Path (Final Destination): {userModsPath}", "SteamCmdDownloader");

            // Final result accumulators
            var finalSucceededItems = new List<DownloadItem>();
            var finalFailedItems = new List<DownloadItem>();
            int lastExitCode = -1; // Store the exit code from the last SteamCMD process run
            string lastAttemptPrimaryLogPath = string.Empty; // Keep track of the last primary log file path for final reporting

            try
            {
                // --- Step 1: Clean Old Download Leftovers ---
                await CleanTemporaryLocationAsync(steamCmdWorkshopContentPath, result, cancellationToken);
                // Clean the downloads state folder (CRUCIAL NEW STEP)
                string downloadsPath = Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", "downloads");
                await CleanDirectoryContentsAsync(downloadsPath, "Workshop Downloads", result, cancellationToken);
                // Clean the temp folder (if it exists)
                string workshopTempPath = Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", "temp");
                await CleanDirectoryContentsAsync(workshopTempPath, "Workshop Temp", result, cancellationToken);
                // --- Delete the Workshop Manifest file (ACF) ---
                string acfFileName = $"appworkshop_294100.acf";
                string acfPath = Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", acfFileName);
                _logger.LogInfo($"Attempting cleanup of Workshop Manifest file: {acfPath}", "SteamCmdDownloader");
                result.LogMessages.Add($"Cleaning Workshop Manifest file ({acfFileName})...");
                try
                {
                    if (File.Exists(acfPath))
                    {
                        File.Delete(acfPath);
                        await Task.Delay(50, cancellationToken); // Brief pause for file system

                        if (!File.Exists(acfPath)) // Verify deletion
                        {
                            _logger.LogInfo($"Successfully deleted Workshop Manifest file: {acfPath}", "SteamCmdDownloader");
                            result.LogMessages.Add("Workshop Manifest file deleted.");
                        }
                        else
                        {
                            // Log warning if deletion verification failed, but don't necessarily abort yet.
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
                catch (OperationCanceledException) { throw; } // Propagate cancellation
                catch (Exception ex)
                {
                    // Log a warning if the ACF file couldn't be deleted. It might indicate a permission issue
                    // or the file being locked, which could still cause problems later.
                    _logger.LogWarning($"Failed to delete Workshop Manifest file '{acfPath}': {ex.Message}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Failed to delete Workshop Manifest file '{acfFileName}'. Error: {ex.Message}");
                    // Decide if this should be a fatal error or just a warning. For now, warning seems reasonable.
                }
                cancellationToken.ThrowIfCancellationRequested();

                // --- Step 2: Retry Loop for Downloading ---
                for (int attempt = 1; attempt <= MaxDownloadAttempts && itemsToAttemptIds.Any(); attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInfo($"--- Starting Download Attempt {attempt} of {MaxDownloadAttempts} for {itemsToAttemptIds.Count} items ---", "SteamCmdDownloader");
                    result.LogMessages.Add($"--- Attempt {attempt}/{MaxDownloadAttempts} for {itemsToAttemptIds.Count} items ---");

                    string scriptId = Guid.NewGuid().ToString("N").Substring(0, 8); // Unique ID for this attempt's files
                    string scriptPath = Path.Combine(steamCmdExeDir, $"rimsharp_dl_script_{scriptId}.txt");
                    string primaryLogPath = Path.Combine(steamCmdExeDir, $"rimsharp_dl_log_{scriptId}.log");
                    lastAttemptPrimaryLogPath = primaryLogPath; // Store for final message

                    var currentAttemptItems = validItems.Where(i => itemsToAttemptIds.Contains(i.SteamId!)).ToList();
                    if (!currentAttemptItems.Any()) { break; } // Should be redundant

                    // --- 2a. Create Script ---
                    if (!await GenerateScriptAsync(steamCmdExeDir, scriptId, steamCmdSteamAppsActualPath, currentAttemptItems, validate, result, cancellationToken))
                    {
                        finalFailedItems.AddRange(currentAttemptItems); // Mark all current items as failed
                        itemsToAttemptIds.Clear(); // Stop retrying
                        goto EndProcessing; // Use goto to jump past further attempts and processing
                    }
                    cancellationToken.ThrowIfCancellationRequested();

                    // --- 2b. Execute SteamCMD ---
                    _logger.LogInfo($"Attempt {attempt}: Executing SteamCMD...", "SteamCmdDownloader");
                    result.LogMessages.Add($"Attempt {attempt}: Executing SteamCMD...");
                    DateTime filterTime = DateTime.Now.AddSeconds(-3); // Use a slightly earlier time for safety margin for log parsing
                    _logger.LogInfo($"Attempt {attempt}: Parsing logs starting from >= {filterTime:O}", "SteamCmdDownloader");

                    try
                    {
                        lastExitCode = await _processRunner.RunSteamCmdAsync(
                            scriptPath,        // Path to the generated script
                            primaryLogPath,    // Path for the primary stdout/stderr log
                            steamCmdExeDir,    // Working directory for the process
                            cancellationToken);
                        result.ExitCode = lastExitCode; // Store the latest exit code
                        _logger.LogInfo($"Attempt {attempt}: SteamCMD process exited with code: {lastExitCode}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: SteamCMD exited ({lastExitCode}).");
                    }
                    catch (FileNotFoundException fnfEx) // Handle cases where runner can't find exe/script
                    {
                        _logger.LogError($"Attempt {attempt}: FAILED to run SteamCMD process: {fnfEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Error running SteamCMD: {fnfEx.Message}. Aborting download operation.");
                        result.ExitCode = -995; // Indicate critical file not found for runner
                        result.OverallSuccess = false;
                        finalFailedItems.AddRange(currentAttemptItems); // Mark all current items as failed
                        itemsToAttemptIds.Clear(); // Stop retrying
                        goto EndProcessing; // Use goto to jump past further attempts and processing
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception runEx) // Catch other potential runner errors
                    {
                        _logger.LogError($"Attempt {attempt}: FAILED unexpectedly during SteamCMD execution: {runEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Unexpected error running SteamCMD: {runEx.Message}.");
                        lastExitCode = -994; // Indicate generic runner error
                        result.ExitCode = lastExitCode;
                        // Log error, but continue to parsing, as some items might have finished before the error.
                    }
                    cancellationToken.ThrowIfCancellationRequested();


                    // --- 2c. Parse Multiple Log Files (MODIFIED) ---
                    _logger.LogInfo($"Attempt {attempt}: Parsing session logs (Workshop, Primary, Content, Bootstrap)...", "SteamCmdDownloader");
                    result.LogMessages.Add($"Attempt {attempt}: Parsing session logs...");

                    var logFilePaths = new SteamCmdLogFilePaths
                    {
                        WorkshopLogPath = workshopLogPath,
                        PrimaryExecutionLogPath = primaryLogPath, // The attempt-specific log
                        ContentLogPath = contentLogPath,
                        BootstrapLogPath = bootstrapLogPath
                    };

                    SteamCmdSessionLogParseResult sessionParseResult = new(); // Default empty result
                    try
                    {
                        sessionParseResult = await _logParser.ParseSteamCmdSessionLogsAsync(
                            logFilePaths,
                            itemsToAttemptIds,  // Only interested in items for this attempt
                            filterTime,
                            cancellationToken);

                        _logger.LogInfo($"Attempt {attempt}: Log parsing complete. Processed {sessionParseResult.ProcessedWorkshopEntryCount} workshop entries. Status: {sessionParseResult.OverallStatus.Flags}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Log parsing complete ({sessionParseResult.ProcessedWorkshopEntryCount} workshop entries). Status: {sessionParseResult.OverallStatus.Flags}");

                        // Add critical messages from the parser to the main download result
                        if (sessionParseResult.CriticalMessages.Any())
                        {
                            result.LogMessages.Add($"--- Critical Messages (Attempt {attempt}) ---");
                            result.LogMessages.AddRange(sessionParseResult.CriticalMessages);
                            result.LogMessages.Add($"--- End Critical Messages ---");
                        }

                        // Optional: Add log samples (maybe only on failure or first attempt)
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
                        // Continue, relying on subsequent attempts or file checks.
                    }


                    // --- 2d. Update Overall Status and Prepare for Next Attempt (MODIFIED) ---
                    var nextAttemptFailedIds = new HashSet<string>();
                    int newlySucceededThisAttempt = 0;
                    int stillFailingThisAttempt = 0;

                    // Check for critical session failures first
                    if (sessionParseResult.OverallStatus.HasLoginFailed ||
                        sessionParseResult.OverallStatus.HasDiskError || // Treat disk errors as likely fatal for the attempt
                        sessionParseResult.OverallStatus.HasScriptError) // Script errors usually mean nothing worked
                    {
                        _logger.LogWarning($"Attempt {attempt}: Critical session failure detected ({sessionParseResult.OverallStatus.Flags}). Marking all attempted items as failed for this attempt.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Attempt {attempt}: Critical session failure detected ({sessionParseResult.OverallStatus.Flags}). Assuming items failed.");
                        nextAttemptFailedIds.UnionWith(itemsToAttemptIds); // All items attempted need retry
                        stillFailingThisAttempt = itemsToAttemptIds.Count;
                    }
                    else // Process item results if no critical session failure
                    {
                        // Iterate through the items we *attempted* in this cycle
                        foreach (string currentItemId in itemsToAttemptIds)
                        {
                            bool isNowConsideredSuccess = false;
                            DateTime latestTimestampForId = DateTime.MinValue;
                            string? failureReasonForId = "Unknown error or no log entry";

                            // Check if the comprehensive parser found a result for this item *in this attempt*
                            if (sessionParseResult.WorkshopItemResults.TryGetValue(currentItemId, out var attemptResult))
                            {
                                // Update the *overall* status tracker (finalSessionResults) ONLY if this new log entry is more recent
                                if (!finalSessionResults.TryGetValue(currentItemId, out var existingFinalStatus) || attemptResult.Timestamp > existingFinalStatus.Timestamp)
                                {
                                    finalSessionResults[currentItemId] = attemptResult; // Update with the newer status
                                    _logger.LogDebug($"Attempt {attempt}: Updated final status for {currentItemId} based on log at {attemptResult.Timestamp:O}. Success: {attemptResult.Success}");
                                }
                            }

                            // Now, check the LATEST known status from the overall tracker (finalSessionResults)
                            if (finalSessionResults.TryGetValue(currentItemId, out var latestOverallStatus))
                            {
                                isNowConsideredSuccess = latestOverallStatus.Success;
                                latestTimestampForId = latestOverallStatus.Timestamp;
                                failureReasonForId = latestOverallStatus.Reason;
                            }

                            // Decide if this item needs to be retried based on its latest known status
                            if (!isNowConsideredSuccess)
                            {
                                nextAttemptFailedIds.Add(currentItemId); // Add to the list for the next attempt
                                stillFailingThisAttempt++;
                                _logger.LogWarning($"Attempt {attempt}: Item {currentItemId} marked as FAILED (Reason: {failureReasonForId ?? "N/A"}, Last Log: {latestTimestampForId:O})", "SteamCmdDownloader");
                            }
                            else
                            {
                                newlySucceededThisAttempt++;
                                _logger.LogInfo($"Attempt {attempt}: Item {currentItemId} marked as SUCCEEDED (Log at {latestTimestampForId:O})", "SteamCmdDownloader");
                            }
                        }
                    } // --- End else block (no critical session failure) ---

                    _logger.LogInfo($"Attempt {attempt} Summary: Succeeded now: {newlySucceededThisAttempt}, Still Failing: {stillFailingThisAttempt}, Items for next attempt: {nextAttemptFailedIds.Count}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Attempt {attempt} Summary: Succeeded now: {newlySucceededThisAttempt}, Still Failing: {stillFailingThisAttempt}.");

                    // Update the set of items for the next loop iteration
                    itemsToAttemptIds = nextAttemptFailedIds;

                    // Optional: Add a small delay before the next retry
                    if (itemsToAttemptIds.Any() && attempt < MaxDownloadAttempts)
                    {
                        _logger.LogInfo($"Waiting 1 second before attempt {attempt + 1}...", "SteamCmdDownloader");
                        await Task.Delay(1000, cancellationToken);
                    }

                    // --- 2e. Clean up script file for this attempt ---
                    CleanupScriptFile(scriptPath);

                } // --- End of Retry Loop ---


            EndProcessing: // Label used by goto statements on critical failure

                // --- Step 3: Determine Final Success/Failure based on 'finalSessionResults' ---
                _logger.LogInfo("Consolidating results after all download attempts...", "SteamCmdDownloader");
                result.LogMessages.Add("Consolidating results after all attempts...");

                var provisionallySucceededIds = new HashSet<string>();
                var finalFailedIdsBasedOnLog = new HashSet<string>();

                // Check if the logs were usable if we expected results but got none
                if (!finalSessionResults.Any() && requestedIds.Any() && !File.Exists(workshopLogPath) && !File.Exists(lastAttemptPrimaryLogPath)) // Check primary log too if workshop missing
                {
                    _logger.LogError($"Neither Workshop log '{workshopLogPath}' nor last primary log '{lastAttemptPrimaryLogPath}' found or usable. Assuming all {requestedIds.Count} items failed.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Error: Key log files unusable after all attempts. Assuming all items failed.");
                    finalFailedIdsBasedOnLog.UnionWith(requestedIds); // Mark all as failed
                }
                else
                {
                    // Determine final status for each initially requested ID based on the log results
                    foreach (var requestedId in requestedIds)
                    {
                        if (finalSessionResults.TryGetValue(requestedId, out var latestResult) && latestResult.Success)
                        {
                            // Log indicated success at some point, mark for processing
                            provisionallySucceededIds.Add(requestedId);
                            _logger.LogInfo($"Item {requestedId} final status from logs: SUCCEEDED (Log entry at {latestResult.Timestamp:O})", "SteamCmdDownloader");
                        }
                        else
                        {
                            // Log indicated failure, or no success entry was ever found
                            finalFailedIdsBasedOnLog.Add(requestedId);
                            string reason = finalSessionResults.TryGetValue(requestedId, out var failResult) ? (failResult.Reason ?? "Unknown/NoLog") : "No success result found";
                            DateTime? failTime = finalSessionResults.TryGetValue(requestedId, out failResult) ? failResult.Timestamp : (DateTime?)null;
                            _logger.LogWarning($"Item {requestedId} final status from logs: FAILED (Reason: {reason}, Last Log At: {failTime?.ToString("O") ?? "N/A"})", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {requestedId} failed download (Reason: {reason})");
                        }
                    }
                }
                // Add items deemed failed by logs to the final failure list immediately
                finalFailedItems.AddRange(finalFailedIdsBasedOnLog.Select(id => itemLookup[id]));


                // --- Step 4: Process Provisionally Succeeded Items (using ItemProcessor) ---
                await ProcessSucceededItemsAsync(
                    provisionallySucceededIds,
                    itemLookup,
                    steamCmdWorkshopContentPath,
                    userModsPath,
                    result,
                    finalSucceededItems,
                    finalFailedItems,
                    cancellationToken);

                // --- Step 5: Populate Final Result Object ---
                PopulateFinalResult(result, finalSucceededItems, finalFailedItems, validItems.Any());


                // --- Step 6: Define Overall Success ---
                SetOverallSuccessAndLogFinalStatus(result, validItems.Any(), lastExitCode);

            }
            catch (OperationCanceledException)
            {
                HandleCancellation(result, validItems, finalSucceededItems, finalFailedItems);
            }
            catch (Exception ex) // Catch unexpected exceptions during the orchestration
            {
                HandleUnexpectedException(ex, result, validItems, finalSucceededItems, finalFailedItems);
            }
            finally
            {
                // Add messages indicating where log files are kept for debugging
                LogFinalLogFilePaths(lastAttemptPrimaryLogPath, workshopLogPath, contentLogPath, bootstrapLogPath, result);
            }

            return result;
        }


        // --- Helper Methods ---

        private async Task<bool> EnsurePathValidityAsync(string userModsPath, SteamCmdDownloadResult result)
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
            const int maxRetries = 3; // Number of times to retry deleting an item
            const int retryDelayMilliseconds = 250; // Wait time between retries

            _logger.LogInfo($"Attempting robust cleanup of temporary download location: {tempPath}", "SteamCmdDownloader");
            result.LogMessages.Add($"Cleaning temporary download location (before start, Max Retries: {maxRetries})...");

            if (!Directory.Exists(tempPath))
            {
                _logger.LogInfo($"Temporary download location does not exist, skipping cleanup: {tempPath}", "SteamCmdDownloader");
                result.LogMessages.Add("Temporary download location does not exist, no cleanup needed.");
                try
                {
                    // Ensure parent directory exists so SteamCMD can create subdirs later
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
                // Get all file system entries (files and subdirectories) directly within the tempPath
                itemsToDelete = Directory.GetFileSystemEntries(tempPath);
                _logger.LogDebug($"Found {itemsToDelete.Length} file system item(s) in temp location '{tempPath}' to remove.", "SteamCmdDownloader");
            }
            catch (Exception listEx)
            {
                _logger.LogError($"Failed to list contents of temporary directory '{tempPath}': {listEx.Message}", "SteamCmdDownloader");
                result.LogMessages.Add($"Error: Could not list contents of temporary download location. Cleanup aborted. Check permissions.");
                // Can't proceed if we can't list items.
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
                bool isDirectory = Directory.Exists(itemPath); // Check type just before attempting delete

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
                            // Double check it still exists before delete attempt
                            if (Directory.Exists(itemPath))
                            {
                                Directory.Delete(itemPath, true); // Recursive delete for directories
                                _logger.LogDebug($"Successfully deleted directory '{itemName}' on attempt {attempt}.", "SteamCmdDownloader");
                            }
                            else
                            {
                                _logger.LogDebug($"Directory '{itemName}' disappeared before delete attempt {attempt}. Skipping.", "SteamCmdDownloader");
                            }
                        }
                        else // Assume it's a file if not a directory (or check File.Exists for certainty)
                        {
                            if (File.Exists(itemPath)) // Double check it exists
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
                        break; // Exit retry loop on success
                    }
                    catch (OperationCanceledException) { throw; } // Don't swallow cancellation
                    catch (IOException ioEx) // Common: File in use, network path issues
                    {
                        lastException = ioEx;
                        _logger.LogDebug($"Attempt {attempt}/{maxRetries} failed to delete '{itemName}': {ioEx.GetType().Name} - {ioEx.Message}", "SteamCmdDownloader");
                    }
                    catch (UnauthorizedAccessException uaEx) // Common: Permissions
                    {
                        lastException = uaEx;
                        _logger.LogWarning($"Attempt {attempt}/{maxRetries} failed to delete '{itemName}' due to permissions: {uaEx.Message}", "SteamCmdDownloader");
                        // Retrying might not help permissions, but we'll try anyway. Could break early? Maybe not.
                    }
                    catch (Exception ex) // Catch unexpected errors during delete
                    {
                        lastException = ex;
                        _logger.LogError($"Attempt {attempt}/{maxRetries} failed to delete '{itemName}' with unexpected error: {ex.GetType().Name} - {ex.Message}", "SteamCmdDownloader");
                    }

                    // If delete failed, wait before retrying (unless it's the last attempt)
                    if (!deletedSuccessfully && attempt < maxRetries)
                    {
                        _logger.LogDebug($"Waiting {retryDelayMilliseconds}ms before retry {attempt + 1} for '{itemName}'.", "SteamCmdDownloader");
                        await Task.Delay(retryDelayMilliseconds, cancellationToken);
                    }
                } // End retry loop

                if (!deletedSuccessfully)
                {
                    cleanupErrors++;
                    string errorType = lastException?.GetType().Name ?? "Unknown Error";
                    string errorMessage = lastException?.Message ?? "No specific error message.";
                    _logger.LogError($"Failed to delete '{itemName}' after {maxRetries} attempts. Last error: {errorType} - {errorMessage}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Failed to clean up '{itemName}' after {maxRetries} attempts ({errorType}). Check logs/permissions.");
                    // Optionally add more details from lastException if needed
                    // result.LogMessages.Add($"    Details: {errorMessage}");
                }

            } // End foreach loop over items

            // Log cleanup summary
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


            // Post-Cleanup Verification (optional but recommended)
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

            cancellationToken.ThrowIfCancellationRequested(); // Final check after potentially long cleanup
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
                result.ExitCode = -996; // Indicate script generation failure
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
                    try { subDir.Delete(true); } // Recursive delete for subdirs
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
        }


        private async Task ProcessSucceededItemsAsync(
            IEnumerable<string> successIds,
            Dictionary<string, DownloadItem> itemLookup,
            string sourceBasePath,
            string targetBasePath,
            SteamCmdDownloadResult result,
            List<DownloadItem> finalSucceededItems,
            List<DownloadItem> finalFailedItems,
            CancellationToken cancellationToken)
        {
            _logger.LogInfo($"Processing {successIds.Count()} items marked as successful by logs...", "SteamCmdDownloader");
            result.LogMessages.Add($"Processing {successIds.Count()} successfully downloaded items...");

            foreach (var successId in successIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadItem currentItem = itemLookup[successId];
                string sourceDownloadPath = Path.Combine(sourceBasePath, successId);
                string targetModPath = Path.Combine(targetBasePath, successId);

                _logger.LogInfo($"Handing off item {successId} ('{currentItem.Name}') to Item Processor.", "SteamCmdDownloader");
                result.LogMessages.Add($"--- Processing Item {successId} ({currentItem.Name}) ---");
                bool processedOk = false;
                try
                {
                    // Delegate the complex file operations to the dedicated processor
                    processedOk = await _itemProcessor.ProcessItemAsync(
                        currentItem,          // Item metadata
                        sourceDownloadPath,   // Source path (in steamcmd download area)
                        targetModPath,        // Target path (in game mods folder)
                        BackupSuffix,         // Suffix for backups
                        cancellationToken);

                    // Retrieve and add detailed log messages from the processor for this item
                    var processorLogs = _itemProcessor.GetLogMessages();
                    result.LogMessages.AddRange(processorLogs);
                    result.LogMessages.Add($"--- Finished Processing Item {successId} ---");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception procEx) // Catch unexpected errors *during* the processor call
                {
                    _logger.LogError($"Item {successId}: Unexpected error during item processing step: {procEx.Message}", "SteamCmdDownloader");
                    result.LogMessages.Add($"CRITICAL ERROR processing item {successId} ({currentItem.Name}): {procEx.Message}");
                    processedOk = false; // Ensure marked as failed
                                         // Add any logs collected by the processor before the exception occurred
                    result.LogMessages.AddRange(_itemProcessor.GetLogMessages());
                    result.LogMessages.Add($"--- Finished Processing Item {successId} (with error) ---");
                }


                // Update final lists based on the processor's success/failure outcome
                if (processedOk)
                {
                    finalSucceededItems.Add(currentItem);
                    _logger.LogInfo($"Item {successId} successfully processed by Item Processor.", "SteamCmdDownloader");
                }
                else
                {
                    // Ensure this item is marked as failed if it wasn't already
                    if (!finalFailedItems.Any(fi => fi != null && fi.SteamId == currentItem.SteamId))
                    {
                        finalFailedItems.Add(currentItem);
                    }
                    _logger.LogWarning($"Item {successId} processing FAILED by Item Processor.", "SteamCmdDownloader");
                }

            } // End foreach provisionallySucceededId
        }

        private void PopulateFinalResult(SteamCmdDownloadResult result, List<DownloadItem> finalSucceededItems, List<DownloadItem> finalFailedItems, bool hadValidItems)
        {
            // Add successfully processed items
            result.SucceededItems.AddRange(finalSucceededItems);
            // Consolidate failed items, ensuring no duplicates and removing any that somehow succeeded processing
            var distinctFailedItems = finalFailedItems
                                        .Where(failedItem => failedItem != null && !result.SucceededItems.Any(succeededItem => succeededItem.SteamId == failedItem.SteamId))
                                        .GroupBy(item => item.SteamId) // Ensure unique by ID
                                        .Select(group => group.First())
                                        .ToList();
            result.FailedItems.Clear(); // Clear any previous additions
            result.FailedItems.AddRange(distinctFailedItems);

            _logger.LogInfo($"Final results: Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
            if (result.SucceededItems.Any()) _logger.LogDebug($"Succeeded IDs: {string.Join(", ", result.SucceededItems.Select(i => i.SteamId))}", "SteamCmdDownloader");
            if (result.FailedItems.Any()) _logger.LogWarning($"Failed IDs: {string.Join(", ", result.FailedItems.Select(i => i.SteamId))}", "SteamCmdDownloader");
        }

        private void SetOverallSuccessAndLogFinalStatus(SteamCmdDownloadResult result, bool hadValidItems, int lastExitCode)
        {
            // Overall success means no items failed, AND (either some items succeeded OR no valid items were requested initially).
            result.OverallSuccess = !result.FailedItems.Any() && (result.SucceededItems.Any() || !hadValidItems);

            // Add final summary status messages based on outcome
            if (result.OverallSuccess && hadValidItems)
            {
                _logger.LogInfo("Download operation completed successfully for all requested items.", "SteamCmdDownloader");
                result.LogMessages.Add("Download completed successfully for all items.");
            }
            else if (result.OverallSuccess && !hadValidItems)
            {
                // Message already added at start: "Download skipped: No valid workshop items provided."
            }
            else if (!result.OverallSuccess && result.FailedItems.Any()) // Failures exist
            {
                _logger.LogError($"Download operation finished with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}. Last SteamCMD Exit Code: {lastExitCode}.", "SteamCmdDownloader");
                result.LogMessages.Add($"Download completed with {result.FailedItems.Count} failure(s). Last SteamCMD Exit Code: {lastExitCode}. Check messages/logs for details.");
            }
            else // Should not happen if logic is correct (e.g., OverallSuccess is false but no failed items)
            {
                _logger.LogWarning($"Download operation finished with unclear status. OverallSuccess={result.OverallSuccess}, Succeeded={result.SucceededItems.Count}, Failed={result.FailedItems.Count}, LastExitCode={lastExitCode}", "SteamCmdDownloader");
                result.LogMessages.Add($"Download finished with unclear status.");
                result.OverallSuccess = false; // Force false if state is inconsistent
            }

            _logger.LogInfo($"Download processing finished. Overall success flag: {result.OverallSuccess}", "SteamCmdDownloader");
        }

        private void HandleCancellation(SteamCmdDownloadResult result, List<DownloadItem> validItems, List<DownloadItem> finalSucceededItems, List<DownloadItem> finalFailedItems)
        {
            _logger.LogWarning("Download operation cancelled by user", "SteamCmdDownloader");
            result.LogMessages.Add("Download operation cancelled.");
            result.OverallSuccess = false;
            // Mark any items not already succeeded as failed
            var succeededSoFarIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet();
            var newlyFailed = validItems.Where(i => i?.SteamId != null && !succeededSoFarIds.Contains(i.SteamId));
            finalFailedItems.AddRange(newlyFailed);
            // Repopulate lists cleanly after potential partial processing
            result.SucceededItems.Clear();
            result.SucceededItems.AddRange(finalSucceededItems);
            var distinctFailed = finalFailedItems.Where(i => i != null).DistinctBy(i => i.SteamId).ToList();
            result.FailedItems.Clear();
            result.FailedItems.AddRange(distinctFailed);
        }

        private void HandleUnexpectedException(Exception ex, SteamCmdDownloadResult result, List<DownloadItem> validItems, List<DownloadItem> finalSucceededItems, List<DownloadItem> finalFailedItems)
        {
            _logger.LogError($"Download failed with unexpected exception: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
            result.LogMessages.Add($"Download failed with unexpected exception: {ex.Message}");
            result.OverallSuccess = false;
            // Ensure all items not successfully processed are marked failed
            var succeededIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet();
            var newlyFailed = validItems.Where(i => i?.SteamId != null && !succeededIds.Contains(i.SteamId));
            finalFailedItems.AddRange(newlyFailed);
            // Repopulate lists cleanly
            result.SucceededItems.Clear();
            result.SucceededItems.AddRange(finalSucceededItems);
            var distinctFailed = finalFailedItems.Where(i => i != null).DistinctBy(i => i.SteamId).ToList();
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
