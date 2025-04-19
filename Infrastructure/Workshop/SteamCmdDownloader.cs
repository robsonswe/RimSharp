#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Services.Contracts; // Keep for IDialogService, ILoggerService, IPathService

namespace RimSharp.Infrastructure.Workshop
{
    public class SteamCmdDownloader : ISteamCmdDownloader
    {
        private const string RimworldAppId = "294100";
        private const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss";
        private const string BackupSuffix = "_backup"; // Suffix for backup folders

        // Regex for parsing success/failure lines from workshop_log.txt
        private static readonly Regex WorkshopLogSuccessRegex = new Regex(
             @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*OK",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WorkshopLogFailureRegex = new Regex(
            @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*(?!OK\s*$)(\w+[\w\s]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Dependencies
        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _gamePathService;
        private readonly IModService _modService;
        private readonly ISteamCmdFileSystem _fileSystem; // Needed for clearing the cache

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            IDialogService dialogService,
            ILoggerService logger,
            IPathService gamePathService,
            IModService modService,
            ISteamCmdFileSystem fileSystem) // Inject ISteamCmdFileSystem
        {
            _pathService = pathService;
            _installer = installer;
            _dialogService = dialogService;
            _logger = logger;
            _gamePathService = gamePathService;
            _modService = modService;
            _fileSystem = fileSystem; // Store the filesystem service
        }

        public async Task<SteamCmdDownloadResult> DownloadModsAsync(
                        IEnumerable<DownloadItem> itemsToDownload,
                        bool validate,
                        CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("Starting SteamCMD download operation (Temp Download + Move strategy)", "SteamCmdDownloader");
            var result = new SteamCmdDownloadResult();
            bool isSetupComplete = await _installer.CheckSetupAsync();
            string userModsPath = _gamePathService.GetModsPath();

            // --- Initial Checks (Setup, Paths) ---
            if (!isSetupComplete || string.IsNullOrEmpty(_pathService.SteamCmdExePath))
            {
                _logger.LogError("Download failed: SteamCMD is not set up properly.", "SteamCmdDownloader");
                result.LogMessages.Add("Download failed: SteamCMD is not set up.");
                result.OverallSuccess = false;
                return result;
            }
            if (string.IsNullOrEmpty(userModsPath) || !Directory.Exists(Path.GetDirectoryName(userModsPath)))
            {
                _logger.LogError($"Download failed: RimWorld Mods Path is not configured or invalid: '{userModsPath}'", "SteamCmdDownloader");
                result.LogMessages.Add($"Download failed: RimWorld Mods Path is not configured or invalid.");
                result.OverallSuccess = false;
                _dialogService.ShowError("Download Failed", "Cannot download mods because the RimWorld Mods Path is not configured or seems invalid in the application settings.");
                return result;
            }
            // --- End Initial Checks ---

            var validItems = itemsToDownload
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.SteamId) && long.TryParse(i.SteamId.Trim(), out _))
                .ToList();

            if (!validItems.Any())
            {
                _logger.LogInfo("Download skipped: No valid workshop items provided", "SteamCmdDownloader");
                result.LogMessages.Add("Download skipped: No valid workshop items provided.");
                result.OverallSuccess = true; // No work needed, technically success
                return result;
            }

            var itemLookup = validItems.ToDictionary(i => i.SteamId!);
            var requestedIds = itemLookup.Keys.ToHashSet();

            // Stores results parsed from workshop_log.txt for this session
            var sessionResults = new Dictionary<string, (bool Success, DateTime Timestamp, string? Reason)>();

            // --- Path Setup ---
            string? scriptDir = Path.GetDirectoryName(_pathService.SteamCmdExePath);
            if (string.IsNullOrEmpty(scriptDir))
            {
                _logger.LogError("Error: Cannot determine SteamCMD directory from Exe path.", "SteamCmdDownloader");
                result.LogMessages.Add("Error: Cannot determine SteamCMD directory.");
                result.OverallSuccess = false;
                return result;
            }
            string steamCmdSteamAppsActualPath = _pathService.SteamCmdSteamAppsPath;
            if (string.IsNullOrEmpty(steamCmdSteamAppsActualPath))
            {
                _logger.LogError("Error: SteamCMD steamapps path (force_install_dir target) is not configured.", "SteamCmdDownloader");
                result.LogMessages.Add("Error: SteamCMD install path for downloads is not configured.");
                result.OverallSuccess = false;
                return result;
            }
            string steamCmdWorkshopContentPath = _pathService.SteamCmdWorkshopContentPath;
            string workshopLogPath = Path.Combine(_pathService.SteamCmdInstallPath, "logs", "workshop_log.txt");
            string scriptId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string scriptPath = Path.Combine(scriptDir, $"rimsharp_dl_script_{scriptId}.txt");
            string primaryLogPath = Path.Combine(scriptDir, $"rimsharp_dl_log_{scriptId}.log");
            // --- End Path Setup ---

            _logger.LogDebug($"Script path: {scriptPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Primary log path (+log_file): {primaryLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Workshop log path (detailed status): {workshopLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"SteamCMD Download Target (force_install_dir): {steamCmdSteamAppsActualPath}", "SteamCmdDownloader");
            _logger.LogDebug($"User Mods Path (Final Destination): {userModsPath}", "SteamCmdDownloader");

            DateTime startTime = DateTime.Now;
            DateTime filterTime = startTime.AddSeconds(-5); // Use a slightly earlier time for safety margin
            _logger.LogInfo($"Operation started at {startTime:O}. Filtering log entries >= {filterTime:O}", "SteamCmdDownloader");

            // Lists for final results
            var finalSucceededItems = new List<DownloadItem>();
            var finalFailedItems = new List<DownloadItem>();

            try
            {
                // --- <<< ALWAYS CLEAR DEPOT CACHE (Step 1) >>> ---
                _logger.LogInfo("Attempting to clear SteamCMD depot cache before download (standard procedure)...", "SteamCmdDownloader");
                result.LogMessages.Add("Clearing SteamCMD depot cache...");
                try
                {
                    // Use the injected filesystem service to clear the cache
                    bool clearSuccess = await _fileSystem.ClearDepotCacheAsync();
                    if (clearSuccess)
                    {
                        _logger.LogInfo("SteamCMD depot cache cleared successfully.", "SteamCmdDownloader");
                        result.LogMessages.Add("SteamCMD depot cache cleared successfully.");
                    }
                    else
                    {
                         // Log error but continue - download might still work, or might fail more clearly
                         _logger.LogError("Failed to clear SteamCMD depot cache prior to download. Proceeding anyway.", "SteamCmdDownloader");
                         result.LogMessages.Add("Warning: Failed to clear SteamCMD depot cache before download. Proceeding...");
                    }
                }
                catch (Exception ex)
                {
                     // Log error but continue
                     _logger.LogError($"Exception occurred while clearing depot cache before download: {ex.Message}", "SteamCmdDownloader");
                     result.LogMessages.Add($"Warning: An exception occurred while clearing depot cache before download: {ex.Message}. Proceeding...");
                }
                cancellationToken.ThrowIfCancellationRequested(); // Allow cancellation after cache clear attempt
                // --- <<< END ALWAYS CLEAR DEPOT CACHE >>> ---


                // --- Clean Temp Download Location (Step 2) ---
                _logger.LogInfo($"Attempting to clean temporary download location: {steamCmdWorkshopContentPath}", "SteamCmdDownloader");
                result.LogMessages.Add("Cleaning temporary download location...");
                if (Directory.Exists(steamCmdWorkshopContentPath))
                {
                    int cleanupErrors = 0;
                    string[] subDirectories = Directory.GetDirectories(steamCmdWorkshopContentPath);
                    _logger.LogDebug($"Found {subDirectories.Length} existing item(s) in temp location to remove.", "SteamCmdDownloader");

                    foreach (string subDirPath in subDirectories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            _logger.LogDebug($"Attempting to delete: {subDirPath}", "SteamCmdDownloader");
                            Directory.Delete(subDirPath, true);
                            await Task.Yield(); // Give FS a moment
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            cleanupErrors++;
                            _logger.LogWarning($"Failed to delete directory during cleanup: {subDirPath}. Error: {ex.Message}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Warning: Failed to clean up '{Path.GetFileName(subDirPath)}' from temp location.");
                        }
                    }
                    // Log cleanup results
                    if (cleanupErrors == 0 && subDirectories.Length > 0)
                    {
                        _logger.LogInfo("Temporary download location cleaned successfully.", "SteamCmdDownloader");
                        result.LogMessages.Add("Temporary download location cleaned.");
                    }
                    else if (cleanupErrors > 0)
                    {
                        _logger.LogWarning($"Finished cleaning temporary location with {cleanupErrors} error(s).", "SteamCmdDownloader");
                        result.LogMessages.Add("Finished cleaning temporary location (some items might remain).");
                    }
                    else
                    {
                        _logger.LogInfo("Temporary download location was already empty or cleanup not needed.", "SteamCmdDownloader");
                        result.LogMessages.Add("Temporary download location clear.");
                    }

                    // Post-Cleanup Verification
                    string[] remainingItems = Directory.GetFileSystemEntries(steamCmdWorkshopContentPath);
                    if (remainingItems.Length > 0)
                    {
                        _logger.LogWarning($"Cleanup verification FAILED. {remainingItems.Length} items still exist in '{steamCmdWorkshopContentPath}'. First item: '{remainingItems[0]}'. Check permissions or file locks.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Warning: Verification check found items remaining in the temporary download folder after cleanup attempt.");
                    }
                    else
                    {
                        _logger.LogInfo("Cleanup verification successful. Temporary download location is empty.", "SteamCmdDownloader");
                    }
                }
                else
                {
                    _logger.LogInfo($"Temporary download location does not exist, skipping cleanup: {steamCmdWorkshopContentPath}", "SteamCmdDownloader");
                    result.LogMessages.Add("Temporary download location does not exist, no cleanup needed.");
                    try { Directory.CreateDirectory(Path.GetDirectoryName(steamCmdWorkshopContentPath) ?? steamCmdSteamAppsActualPath); } // Ensure parent exists
                    catch (Exception ex) { _logger.LogWarning($"Could not ensure parent directory exists for temp location: {ex.Message}", "SteamCmdDownloader"); }
                }
                cancellationToken.ThrowIfCancellationRequested(); // Check cancellation after temp cleanup


                // --- 3. Create Script ---
                _logger.LogInfo("Generating SteamCMD script...", "SteamCmdDownloader");
                result.LogMessages.Add("Generating SteamCMD script...");
                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine($"force_install_dir \"{steamCmdSteamAppsActualPath}\"");
                scriptBuilder.AppendLine("login anonymous");
                string downloadCommand = $"workshop_download_item {RimworldAppId}";
                string validateSuffix = validate ? " validate" : "";
                foreach (var item in validItems)
                {
                    scriptBuilder.AppendLine($"{downloadCommand} {item.SteamId!.Trim()}{validateSuffix}");
                    cancellationToken.ThrowIfCancellationRequested();
                }
                scriptBuilder.AppendLine("quit");
                await File.WriteAllTextAsync(scriptPath, scriptBuilder.ToString(), cancellationToken);
                _logger.LogInfo($"SteamCMD script generated for {validItems.Count} items", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD script generated ({validItems.Count} items).");
                cancellationToken.ThrowIfCancellationRequested();

                // --- 4. Execute SteamCMD ---
                _logger.LogInfo($"Executing SteamCMD...", "SteamCmdDownloader");
                result.LogMessages.Add($"Executing SteamCMD...");
                if (File.Exists(primaryLogPath)) { try { File.Delete(primaryLogPath); } catch { /* Ignore */ } }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _pathService.SteamCmdExePath!,
                    Arguments = $"+runscript \"{scriptPath}\" +log_file \"{primaryLogPath}\"",
                    UseShellExecute = true, // Keep true to show window, easier debugging
                    RedirectStandardOutput = false, // Must be false if UseShellExecute=true
                    RedirectStandardError = false, // Must be false if UseShellExecute=true
                    CreateNoWindow = false, // Set to false to show window
                    WorkingDirectory = scriptDir,
                };

                using var process = new Process { StartInfo = processStartInfo };

                process.Start();
                _logger.LogInfo($"SteamCMD process started (PID: {process.Id})", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process started (PID: {process.Id}). Waiting for exit...");

                await process.WaitForExitAsync(cancellationToken); // Use helper extension if available or implement timeout logic
                result.ExitCode = process.ExitCode;

                _logger.LogInfo($"SteamCMD process exited with code: {process.ExitCode}", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process exited with code: {process.ExitCode}.");

                cancellationToken.ThrowIfCancellationRequested();

                // --- 5. Parse Workshop Log File ---
                bool workshopLogParsed = false;
                int processedLogEntryCount = 0;

                _logger.LogInfo($"Attempting to parse workshop log ({workshopLogPath}) using filter time >= {filterTime:O}", "SteamCmdDownloader");
                result.LogMessages.Add($"Attempting to parse workshop log using filter time >= {filterTime:O}");

                if (File.Exists(workshopLogPath))
                {
                    try
                    {
                        string[] logLines;
                        // Use FileShare.ReadWrite to avoid locking issues if SteamCMD is slow closing it
                        using (var fs = new FileStream(workshopLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var sr = new StreamReader(fs))
                        {
                            var linesList = new List<string>();
                            string? line;
                            while ((line = await sr.ReadLineAsync(cancellationToken)) != null)
                            {
                                linesList.Add(line);
                            }
                            logLines = linesList.ToArray();
                        }

                        _logger.LogDebug($"Found workshop log with {logLines.Length} lines", "SteamCmdDownloader");
                        result.LogMessages.Add($"Found workshop log with {logLines.Length} lines");

                        // Log sample for debugging
                        var logSample = logLines.TakeLast(Math.Min(50, logLines.Length));
                        result.LogMessages.Add("=== WORKSHOP LOG SAMPLE (last 50 lines) ===");
                        result.LogMessages.AddRange(logSample);
                        result.LogMessages.Add("=== END OF WORKSHOP LOG SAMPLE ===");


                        foreach (var line in logLines)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool matched = false;

                            Match successMatch = WorkshopLogSuccessRegex.Match(line);
                            if (successMatch.Success)
                            {
                                string timestampStr = successMatch.Groups[1].Value;
                                string id = successMatch.Groups[2].Value;
                                if (DateTime.TryParseExact(timestampStr, LogTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entryTime))
                                {
                                    if (entryTime >= filterTime && requestedIds.Contains(id))
                                    {
                                        processedLogEntryCount++;
                                        _logger.LogDebug($"TIMESTAMP MATCH (Success): ID {id} at {entryTime:O}", "SteamCmdDownloader");
                                        // Store the latest result for this ID within the session
                                        if (!sessionResults.TryGetValue(id, out var existing) || entryTime > existing.Timestamp)
                                        {
                                            sessionResults[id] = (Success: true, Timestamp: entryTime, Reason: null);
                                        }
                                        matched = true;
                                    }
                                } else { _logger.LogWarning($"Could not parse timestamp '{timestampStr}' in workshop log line: {line}", "SteamCmdDownloader"); }
                            }

                            if (!matched) // Only check failure if success didn't match
                            {
                                Match failureMatch = WorkshopLogFailureRegex.Match(line);
                                if (failureMatch.Success)
                                {
                                    string timestampStr = failureMatch.Groups[1].Value;
                                    string id = failureMatch.Groups[2].Value;
                                    string reason = failureMatch.Groups[3].Value.Trim();
                                    if (DateTime.TryParseExact(timestampStr, LogTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entryTime))
                                    {
                                        if (entryTime >= filterTime && requestedIds.Contains(id))
                                        {
                                            processedLogEntryCount++;
                                            _logger.LogWarning($"TIMESTAMP MATCH (Failure): ID {id} at {entryTime:O}, Reason: {reason}", "SteamCmdDownloader");
                                            // Store the latest result for this ID within the session
                                            if (!sessionResults.TryGetValue(id, out var existing) || entryTime > existing.Timestamp)
                                            {
                                                sessionResults[id] = (Success: false, Timestamp: entryTime, Reason: reason);
                                            }
                                            matched = true;
                                            // No need for cache clear recommendation here, cache cleared preemptively
                                        }
                                    } else { _logger.LogWarning($"Could not parse timestamp '{timestampStr}' in workshop log line: {line}", "SteamCmdDownloader"); }
                                }
                            }
                        } // End foreach line

                        workshopLogParsed = true;
                        _logger.LogInfo($"Workshop log parsing complete. Processed {processedLogEntryCount} relevant entries for this session.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Workshop log parsing complete. Processed {processedLogEntryCount} relevant entries.");
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogError($"Error reading workshop log file {workshopLogPath}: {ioEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error reading workshop log file: {ioEx.Message}");
                        workshopLogParsed = false; // Mark as failed parsing
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Unexpected error parsing workshop log file {workshopLogPath}: {ex.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Unexpected error parsing workshop log file: {ex.Message}");
                        workshopLogParsed = false; // Mark as failed parsing
                    }
                }
                else
                {
                    _logger.LogWarning($"Warning: Workshop log file not found at {workshopLogPath}.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Workshop log file not found at {workshopLogPath}. Cannot confirm item status reliably.");
                    workshopLogParsed = false; // Can't parse if it doesn't exist
                }

                // --- 6. Determine Provisional Success/Failure ---
                var provisionallySucceededIds = new HashSet<string>();
                var initiallyFailedIds = new HashSet<string>(); // IDs that failed based on log or lack of log

                if (!workshopLogParsed && requestedIds.Any())
                {
                    _logger.LogError($"Workshop log unusable. Assuming all {requestedIds.Count} requested items failed initial download check.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Workshop log unusable. Assuming all items failed initial download check.");
                    initiallyFailedIds.UnionWith(requestedIds);
                }
                else
                {
                    foreach (var requestedId in requestedIds)
                    {
                        if (sessionResults.TryGetValue(requestedId, out var latestResult) && latestResult.Success)
                        {
                            provisionallySucceededIds.Add(requestedId);
                            _logger.LogInfo($"Item {requestedId} status from log: SUCCEEDED (Log entry at {latestResult.Timestamp:O})", "SteamCmdDownloader");
                        }
                        else
                        {
                            initiallyFailedIds.Add(requestedId);
                            string reason = sessionResults.TryGetValue(requestedId, out var failResult) ? (failResult.Reason ?? "Unknown") : "No result found in log for this session";
                            DateTime? failTime = sessionResults.TryGetValue(requestedId, out failResult) ? failResult.Timestamp : (DateTime?)null;
                            _logger.LogWarning($"Item {requestedId} status from log: FAILED or MISSING (Reason: {reason}, Log entry at {failTime?.ToString("O") ?? "N/A"})", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {requestedId} failed download or no log entry found this session. Reason: {reason}");
                        }
                    }
                }
                // Add initially failed items to the final failed list now
                finalFailedItems.AddRange(initiallyFailedIds.Select(id => itemLookup[id]));


                // --- 7. Process Provisionally Succeeded Items ---
                _logger.LogInfo($"Processing {provisionallySucceededIds.Count} items marked as successful by logs...", "SteamCmdDownloader");
                result.LogMessages.Add($"Processing {provisionallySucceededIds.Count} potentially successful items...");

                string steamCmdWorkshopBaseDir = _pathService.SteamCmdWorkshopContentPath;

                foreach (var successId in provisionallySucceededIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DownloadItem currentItem = itemLookup[successId];
                    string sourcePath = Path.Combine(steamCmdWorkshopBaseDir, successId);
                    string targetPath = Path.Combine(userModsPath, successId);
                    string backupPath = targetPath + BackupSuffix; // Define backup path
                    bool itemProcessedSuccessfully = false;
                    bool backupCreated = false; // Track if we actually made a backup

                    _logger.LogInfo($"Processing item {successId} ({currentItem.Name}): Source='{sourcePath}', Target='{targetPath}'", "SteamCmdDownloader");

                    // Step 7a: Verify Download Files
                    if (!Directory.Exists(sourcePath))
                    {
                        _logger.LogError($"Item {successId}: Directory '{sourcePath}' not found despite log saying OK. Marking as failed.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error: Item {successId} ({currentItem.Name}) downloaded files not found at expected location. Download failed.");
                        if (!finalFailedItems.Contains(currentItem)) finalFailedItems.Add(currentItem); // Ensure it's marked failed
                        continue; // Skip to next item
                    }

                    try
                    {
                        // Step 7b: Create Timestamps in Source
                        _logger.LogDebug($"Item {successId}: Creating timestamp files in source path '{sourcePath}'", "SteamCmdDownloader");
                        string pubDate = currentItem.PublishDate ?? string.Empty;
                        string stdDate = currentItem.StandardDate ?? string.Empty;
                        await _modService.CreateTimestampFilesAsync(sourcePath, successId, pubDate, stdDate);
                        _logger.LogInfo($"Item {successId}: Timestamp files created successfully in source.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Item {successId}: Timestamp files created.");

                        cancellationToken.ThrowIfCancellationRequested();

                        // --- Step 7c: Prepare Target & Create Backup (MODIFIED LOGIC) ---
                        if (Directory.Exists(targetPath))
                        {
                            _logger.LogInfo($"Item {successId}: Existing directory found at target path '{targetPath}'. Attempting to create backup.", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {successId}: Found existing version. Creating backup...");

                            // 1. Delete any lingering backup from a previous failed attempt
                            if (Directory.Exists(backupPath))
                            {
                                _logger.LogWarning($"Item {successId}: Found lingering backup directory '{backupPath}'. Deleting it before creating new backup.", "SteamCmdDownloader");
                                result.LogMessages.Add($"Item {successId}: Cleaning up old backup...");
                                try
                                {
                                    Directory.Delete(backupPath, true);
                                    await Task.Delay(50, cancellationToken); // Brief pause
                                }
                                catch (Exception bkDelEx)
                                {
                                     _logger.LogError($"Item {successId}: FAILED to delete lingering backup directory '{backupPath}'. Error: {bkDelEx.Message}. Cannot proceed safely.", "SteamCmdDownloader");
                                     result.LogMessages.Add($"Error: Item {successId} ({currentItem.Name}) failed to clean up old backup. Aborting update for this item.");
                                     throw new IOException($"Failed to delete lingering backup '{backupPath}'.", bkDelEx); // Fail this item
                                }
                            }

                            // 2. Rename current target to backup path
                            try
                            {
                                _logger.LogDebug($"Item {successId}: Renaming '{targetPath}' to '{backupPath}'", "SteamCmdDownloader");
                                Directory.Move(targetPath, backupPath);
                                backupCreated = true; // Mark that we have a backup
                                _logger.LogInfo($"Item {successId}: Successfully renamed existing directory to backup '{backupPath}'.", "SteamCmdDownloader");
                                result.LogMessages.Add($"Item {successId}: Backup created as '{Path.GetFileName(backupPath)}'.");
                            }
                            catch (Exception rnEx)
                            {
                                _logger.LogError($"Item {successId}: FAILED to rename existing directory '{targetPath}' to '{backupPath}'. Error: {rnEx.Message}. Cannot proceed safely.", "SteamCmdDownloader");
                                result.LogMessages.Add($"Error: Item {successId} ({currentItem.Name}) failed to create backup. Aborting update for this item.");
                                throw new IOException($"Failed to rename '{targetPath}' to '{backupPath}' for backup.", rnEx); // Fail this item
                            }
                        }
                        else
                        {
                            _logger.LogDebug($"Item {successId}: No existing directory found at target path '{targetPath}'. No backup needed.", "SteamCmdDownloader");
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        // --- Step 7d: Move/Copy Source to Target (MODIFIED ERROR HANDLING) ---
                        bool moveOrCopyAttempted = false;
                        try
                        {
                            string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
                            string? targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));
                            bool crossVolume = sourceRoot != null && targetRoot != null &&
                                               !string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);

                            moveOrCopyAttempted = true;
                            if (crossVolume)
                            {
                                _logger.LogInfo($"Item {successId}: Performing cross-volume copy from '{sourcePath}' to '{targetPath}'", "SteamCmdDownloader");
                                result.LogMessages.Add($"Item {successId}: Copying new version into mods folder (cross-volume)...");
                                await CopyDirectoryRecursivelyAsync(sourcePath, targetPath, cancellationToken);
                                _logger.LogInfo($"Item {successId}: Cross-volume copy complete. Deleting source.", "SteamCmdDownloader");
                                Directory.Delete(sourcePath, true); // Delete source after successful copy
                            }
                            else
                            {
                                _logger.LogInfo($"Item {successId}: Moving directory from source '{sourcePath}' to target '{targetPath}' (same volume)", "SteamCmdDownloader");
                                result.LogMessages.Add($"Item {successId}: Moving new version into mods folder...");
                                Directory.Move(sourcePath, targetPath);
                            }

                            await Task.Delay(50, cancellationToken); // Brief pause for FS

                            // --- Step 7e: Verify Move/Copy Outcome ---
                            if (!Directory.Exists(targetPath) || (Directory.Exists(sourcePath) && !crossVolume)) // If same-volume move, source MUST NOT exist. If cross-volume copy, source WAS deleted.
                            {
                                throw new IOException($"Failed post-operation verification. Target exists: {Directory.Exists(targetPath)}, Source exists after operation: {Directory.Exists(sourcePath)} (Cross-volume: {crossVolume})");
                            }

                            _logger.LogInfo($"Item {successId}: Directory moved/copied successfully to '{targetPath}'.", "SteamCmdDownloader");

                            // --- Step 7f: Delete Backup on Success ---
                            if (backupCreated)
                            {
                                _logger.LogInfo($"Item {successId}: Deleting successful backup '{backupPath}'", "SteamCmdDownloader");
                                result.LogMessages.Add($"Item {successId}: Removing temporary backup...");
                                try
                                {
                                    Directory.Delete(backupPath, true);
                                    _logger.LogInfo($"Item {successId}: Backup deleted successfully.", "SteamCmdDownloader");
                                }
                                catch (Exception bkDelEx)
                                {
                                    // Log as warning - main operation succeeded, but cleanup failed.
                                    _logger.LogWarning($"Item {successId}: FAILED to delete backup directory '{backupPath}' after successful update. Error: {bkDelEx.Message}. Manual cleanup might be needed.", "SteamCmdDownloader");
                                    result.LogMessages.Add($"Warning: Item {successId} ({currentItem.Name}) updated, but failed to remove backup '{Path.GetFileName(backupPath)}'.");
                                }
                            }
                            itemProcessedSuccessfully = true; // Mark as fully successful

                        }
                        catch (Exception moveCopyEx) // Catch failures specifically during move/copy/verify/backup-delete
                        {
                            _logger.LogError($"Item {successId}: FAILED during move/copy or post-op cleanup: {moveCopyEx.Message}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Error: Item {successId} ({currentItem.Name}) failed during file transfer: {moveCopyEx.Message}");
                            itemProcessedSuccessfully = false; // Ensure it's marked as failed

                            // --- Step 7g: Attempt Rollback from Backup ---
                            if (backupCreated)
                            {
                                _logger.LogWarning($"Item {successId}: Attempting to restore backup from '{backupPath}' due to move/copy failure.", "SteamCmdDownloader");
                                result.LogMessages.Add($"Item {successId}: Rolling back to previous version...");

                                // 1. Clean up potentially incomplete target directory
                                if (Directory.Exists(targetPath))
                                {
                                    _logger.LogDebug($"Item {successId}: Deleting potentially incomplete target directory '{targetPath}' before restore.", "SteamCmdDownloader");
                                    try
                                    {
                                        Directory.Delete(targetPath, true);
                                        await Task.Delay(50, cancellationToken);
                                    }
                                    catch (Exception targetDelEx)
                                    {
                                        _logger.LogError($"Item {successId}: CRITICAL - Failed to delete incomplete target '{targetPath}' during rollback. Manual intervention required. Error: {targetDelEx.Message}", "SteamCmdDownloader");
                                        result.LogMessages.Add($"CRITICAL ERROR: Item {successId} rollback failed! Could not delete incomplete new version at '{targetPath}'. Backup is still at '{backupPath}'.");
                                        // Cannot safely restore backup if target delete fails. Leave backup as is.
                                        throw new IOException($"Rollback failed for item {successId}: could not delete partial target '{targetPath}'. Backup remains at '{backupPath}'.", targetDelEx); // Re-throw critical failure
                                    }
                                }

                                // 2. Rename backup back to target
                                try
                                {
                                    _logger.LogDebug($"Item {successId}: Renaming backup '{backupPath}' back to '{targetPath}'.", "SteamCmdDownloader");
                                    Directory.Move(backupPath, targetPath);
                                    _logger.LogInfo($"Item {successId}: Successfully restored backup to '{targetPath}'.", "SteamCmdDownloader");
                                    result.LogMessages.Add($"Item {successId}: Previous version restored successfully.");
                                    backupCreated = false; // Backup no longer exists as backup
                                }
                                catch (Exception restoreEx)
                                {
                                    _logger.LogError($"Item {successId}: CRITICAL - Failed to restore backup by renaming '{backupPath}' to '{targetPath}'. Manual intervention required. Error: {restoreEx.Message}", "SteamCmdDownloader");
                                    result.LogMessages.Add($"CRITICAL ERROR: Item {successId} rollback failed! Could not rename backup '{backupPath}' back. Manual cleanup required.");
                                    // Backup still exists, but target is likely missing or incomplete.
                                    throw new IOException($"Rollback failed for item {successId}: could not rename backup '{backupPath}' back to target. Backup remains.", restoreEx); // Re-throw critical failure
                                }
                            }
                            else if(moveOrCopyAttempted) // Move/Copy failed, but there was no backup (shouldn't happen if target existed, but good practice)
                            {
                                 _logger.LogWarning($"Item {successId}: Move/Copy failed, and no backup was created (target directory likely didn't exist initially). Cleaning up potential partial target '{targetPath}'.", "SteamCmdDownloader");
                                 if (Directory.Exists(targetPath))
                                 {
                                     try { Directory.Delete(targetPath, true); }
                                     catch(Exception partialDelEx) {
                                         _logger.LogError($"Item {successId}: Failed to delete partial target '{targetPath}' after failed move/copy (no backup). Error: {partialDelEx.Message}", "SteamCmdDownloader");
                                         result.LogMessages.Add($"Error: Item {successId} failed post-download cleanup of '{targetPath}'. Manual check needed.");
                                     }
                                 }
                            }
                            // No re-throw needed here if rollback logic handled it, the item is already marked as failed.
                        } // End Catch for Move/Copy/Verify/Backup-Delete
                    }
                    catch (OperationCanceledException) { throw; } // Let cancellation propagate
                    catch (Exception ex) // Catch failures from earlier steps (Timestamp, Initial Backup creation)
                    {
                        _logger.LogError($"Item {successId}: FAILED during pre-move processing: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error processing item {successId} ({currentItem.Name}) before file transfer: {ex.Message}. Downloaded files may remain in SteamCMD data folder. No changes made to mods folder.");
                        itemProcessedSuccessfully = false;
                        // No rollback needed as we failed before modifying the target dir (or during backup creation itself)
                    }

                    // Finalize status for this item
                    if (itemProcessedSuccessfully)
                    {
                        finalSucceededItems.Add(currentItem);
                        result.LogMessages.Add($"Item {successId} ({currentItem.Name}) successfully downloaded and processed.");
                        _logger.LogInfo($"Item {successId} processing SUCCEEDED.", "SteamCmdDownloader");
                    }
                    else
                    {
                        // Ensure it's in the failed list (might have been added earlier or in catch blocks)
                        if (!finalFailedItems.Contains(currentItem))
                        {
                            finalFailedItems.Add(currentItem);
                        }
                         _logger.LogWarning($"Item {successId} processing FAILED.", "SteamCmdDownloader");
                         // Log message already added in the relevant catch block or initial check
                    }

                } // End foreach provisionallySucceededId

                // --- 8. Populate Final Result Object ---
                result.SucceededItems.AddRange(finalSucceededItems);
                // Ensure failed list only contains items NOT in succeeded list and are distinct
                var distinctFailedItems = finalFailedItems
                                            .Where(failed => !result.SucceededItems.Any(succeeded => succeeded.SteamId == failed.SteamId))
                                            .GroupBy(item => item.SteamId) // Group by SteamId to handle potential duplicates
                                            .Select(group => group.First()) // Select the first item from each group
                                            .ToList();
                result.FailedItems.Clear(); // Clear any items added previously
                result.FailedItems.AddRange(distinctFailedItems);

                _logger.LogInfo($"Final results: Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
                 if (result.SucceededItems.Any()) _logger.LogInfo($"Succeeded IDs: {string.Join(", ", result.SucceededItems.Select(i => i.SteamId))}", "SteamCmdDownloader");
                 if (result.FailedItems.Any()) _logger.LogWarning($"Failed IDs: {string.Join(", ", result.FailedItems.Select(i => i.SteamId))}", "SteamCmdDownloader");

                // --- 9. Define Overall Success ---
                // Overall success requires no failed items AND (either items were processed successfully OR no items were requested initially)
                result.OverallSuccess = !result.FailedItems.Any() && (result.SucceededItems.Any() || !validItems.Any());

                // Add more specific status messages based on outcome
                if (result.OverallSuccess && validItems.Any())
                {
                    _logger.LogInfo("Download operation completed successfully for all requested items.", "SteamCmdDownloader");
                    result.LogMessages.Add("Download completed successfully for all items.");
                }
                else if (result.OverallSuccess && !validItems.Any())
                {
                    _logger.LogInfo("Download operation finished. No valid items were provided to download.", "SteamCmdDownloader");
                    result.LogMessages.Add("Download finished. No items requested or none were valid.");
                }
                else if (process.ExitCode != 0)
                {
                    _logger.LogError($"Download operation failed. SteamCMD process reported an error (ExitCode: {process.ExitCode}). Check logs.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Download operation failed. SteamCMD process error (ExitCode: {process.ExitCode}). Check primary log for details.");
                }
                else if (!workshopLogParsed && requestedIds.Any() && result.FailedItems.Count == requestedIds.Count)
                {
                    // Log message about unusable log already added
                    _logger.LogError($"Download operation failed. Could not read/parse the workshop log file ({workshopLogPath}) to confirm status, and all items were marked failed.", "SteamCmdDownloader");
                }
                else if (result.FailedItems.Any())
                {
                    _logger.LogWarning($"Download operation completed with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Download completed with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}. Check messages for details.");
                }
                else // Should ideally not be reached if logic above is correct, but acts as a catch-all
                {
                     _logger.LogWarning($"Download operation finished with unclear status or unexpected state. ExitCode: {process.ExitCode}, Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
                     result.LogMessages.Add($"Download finished with unclear status.");
                     result.OverallSuccess = false; // Mark as not successful in this ambiguous case
                }

                _logger.LogInfo($"Download processing finished. Overall success flag: {result.OverallSuccess}", "SteamCmdDownloader");

            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download operation cancelled by user", "SteamCmdDownloader");
                result.LogMessages.Add("Download operation cancelled.");
                result.OverallSuccess = false;
                // Assign remaining requested items (that didn't succeed) as failed
                var succeededSoFarIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet();
                var newlyFailed = validItems.Where(i => i.SteamId != null && !succeededSoFarIds.Contains(i.SteamId));
                finalFailedItems.AddRange(newlyFailed);
                // Repopulate lists based on final state after cancellation
                result.SucceededItems.Clear();
                result.SucceededItems.AddRange(finalSucceededItems);
                var distinctFailed = finalFailedItems.Distinct().ToList();
                result.FailedItems.Clear();
                result.FailedItems.AddRange(distinctFailed);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download failed with unexpected exception: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
                result.LogMessages.Add($"Download failed with unexpected exception: {ex.Message}");
                result.OverallSuccess = false;
                // Ensure all items not successfully processed are marked failed
                var succeededIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet();
                var newlyFailed = validItems.Where(i => i.SteamId != null && !succeededIds.Contains(i.SteamId));
                finalFailedItems.AddRange(newlyFailed);
                // Repopulate lists based on final state after exception
                result.SucceededItems.Clear();
                result.SucceededItems.AddRange(finalSucceededItems);
                var distinctFailed = finalFailedItems.Distinct().ToList();
                result.FailedItems.Clear();
                result.FailedItems.AddRange(distinctFailed);
                _dialogService.ShowError("Download Failed", $"An unexpected error occurred while running SteamCMD or processing downloads:\n\n{ex.Message}");
            }
            finally
            {
                // Clean up script file
                if (File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); _logger.LogDebug($"Cleaned up script file: {scriptPath}", "SteamCmdDownloader"); }
                    catch (Exception ex) { _logger.LogWarning($"Failed to clean up script file '{scriptPath}': {ex.Message}", "SteamCmdDownloader"); }
                }
                 // Keep log files for diagnosis
                if (File.Exists(primaryLogPath)) { _logger.LogInfo($"Primary log file kept at: {primaryLogPath}", "SteamCmdDownloader"); result.LogMessages.Add($"Primary log file kept at: {primaryLogPath}"); }
                if (File.Exists(workshopLogPath)) { _logger.LogInfo($"Workshop log file kept at: {workshopLogPath}", "SteamCmdDownloader"); result.LogMessages.Add($"Workshop log file kept at: {workshopLogPath}"); }

                // No longer attempting conditional cache clear here - it's done at the start
            }

            return result;
        }

        /// <summary>
        /// Recursively copies a directory and its contents. Handles cancellation.
        /// </summary>
        private async Task CopyDirectoryRecursivelyAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDir}");
            }
            // Ensure the destination directory exists.
            Directory.CreateDirectory(destinationDir);
            cancellationToken.ThrowIfCancellationRequested();

            // Copy files
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, file.Name);
                // Use asynchronous copy if available and preferred, or stick with sync + yield
                // await CopyFileAsync(file.FullName, tempPath, true, cancellationToken); // Example if using async copy helper
                 file.CopyTo(tempPath, true); // Overwrite existing files
                 await Task.Yield(); // Allow other tasks to run briefly, especially during large file copies
            }

            // Recursively copy subdirectories
             DirectoryInfo[] dirs = dir.GetDirectories();
            foreach (DirectoryInfo subdir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, subdir.Name);
                await CopyDirectoryRecursivelyAsync(subdir.FullName, tempPath, cancellationToken);
            }
        }
    }
}