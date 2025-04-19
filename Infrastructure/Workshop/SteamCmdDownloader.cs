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

        private static readonly Regex WorkshopLogSuccessRegex = new Regex(
             @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*OK",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WorkshopLogFailureRegex = new Regex(
            @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*(?!OK\s*$)(\w+[\w\s]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;
        private readonly IPathService _gamePathService; // <<< ADDED for user's mods path
        private readonly IModService _modService;      // <<< ADDED for timestamp creation

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            IDialogService dialogService,
            ILoggerService logger,
            IPathService gamePathService, // <<< ADDED
            IModService modService)      // <<< ADDED
        {
            _pathService = pathService;
            _installer = installer;
            _dialogService = dialogService;
            _logger = logger;
            _gamePathService = gamePathService; // <<< ADDED
            _modService = modService;          // <<< ADDED
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
            if (string.IsNullOrEmpty(userModsPath) || !Directory.Exists(Path.GetDirectoryName(userModsPath))) // Check parent exists for safety
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

            var itemLookup = validItems.ToDictionary(i => i.SteamId!); // SteamId is non-null string key
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
            string workshopLogPath = Path.Combine(_pathService.SteamCmdInstallPath, "logs", "workshop_log.txt"); // Log relative to steamcmd install
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
            DateTime filterTime = startTime.AddSeconds(-5); // Filter logs slightly before start time
            _logger.LogInfo($"Operation started at {startTime:O}. Filtering log entries >= {filterTime:O}", "SteamCmdDownloader");

            // Lists for final results after move/delete operations
            var finalSucceededItems = new List<DownloadItem>();
            var finalFailedItems = new List<DownloadItem>();


            try
            {
                                // --- <<< NEW: Clean Temp Download Location >>> ---
                _logger.LogInfo($"Attempting to clean temporary download location: {steamCmdWorkshopContentPath}", "SteamCmdDownloader");
                result.LogMessages.Add("Cleaning temporary download location...");
                if (Directory.Exists(steamCmdWorkshopContentPath))
                {
                    int cleanupErrors = 0;
                    // Get subdirectories (mod ID folders) within the content path
                    string[] subDirectories = Directory.GetDirectories(steamCmdWorkshopContentPath);
                    _logger.LogDebug($"Found {subDirectories.Length} existing item(s) in temp location to remove.", "SteamCmdDownloader");

                    foreach (string subDirPath in subDirectories)
                    {
                         // Check cancellation before each deletion attempt
                         cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            _logger.LogDebug($"Attempting to delete: {subDirPath}", "SteamCmdDownloader");
                            Directory.Delete(subDirPath, true); // Recursive delete
                            await Task.Yield(); // Give OS a moment
                        }
                        catch (OperationCanceledException) { throw; } // Re-throw cancellation
                        catch (Exception ex)
                        {
                            cleanupErrors++;
                            _logger.LogWarning($"Failed to delete directory during cleanup: {subDirPath}. Error: {ex.Message}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Warning: Failed to clean up '{Path.GetFileName(subDirPath)}' from temp location.");
                            // Continue cleanup despite errors - best effort
                        }
                    }
                    if(cleanupErrors == 0 && subDirectories.Length > 0)
                    {
                         _logger.LogInfo("Temporary download location cleaned successfully.", "SteamCmdDownloader");
                         result.LogMessages.Add("Temporary download location cleaned.");
                    }
                    else if (cleanupErrors > 0)
                    {
                         _logger.LogWarning($"Finished cleaning temporary location with {cleanupErrors} error(s).", "SteamCmdDownloader");
                         result.LogMessages.Add("Finished cleaning temporary location (some items might remain).");
                    }
                    else // No errors, nothing to clean
                    {
                         _logger.LogInfo("Temporary download location was already empty or cleanup not needed.", "SteamCmdDownloader");
                         result.LogMessages.Add("Temporary download location clear.");
                    }
                }
                else
                {
                    _logger.LogInfo($"Temporary download location does not exist, skipping cleanup: {steamCmdWorkshopContentPath}", "SteamCmdDownloader");
                    result.LogMessages.Add("Temporary download location does not exist, no cleanup needed.");
                }
                cancellationToken.ThrowIfCancellationRequested(); // Check cancellation after cleanup attempt
                // --- <<< END Clean Temp Download Location >>> ---

                // --- 1. Create Script ---
                _logger.LogInfo("Generating SteamCMD script...", "SteamCmdDownloader");
                result.LogMessages.Add("Generating SteamCMD script...");
                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine($"force_install_dir \"{steamCmdSteamAppsActualPath}\""); // Point to actual download path
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

                // --- 2. Execute SteamCMD ---
                _logger.LogInfo($"Executing SteamCMD...", "SteamCmdDownloader");
                result.LogMessages.Add($"Executing SteamCMD...");
                if (File.Exists(primaryLogPath)) { try { File.Delete(primaryLogPath); } catch { /* Ignore delete error */ } }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _pathService.SteamCmdExePath!, // Already checked non-null
                    Arguments = $"+runscript \"{scriptPath}\" +log_file \"{primaryLogPath}\"",
                    UseShellExecute = true, // Keep true to show window
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false, // Show the window
                    WorkingDirectory = scriptDir,
                };

                using var process = new Process { StartInfo = processStartInfo };

                process.Start();
                _logger.LogInfo($"SteamCMD process started (PID: {process.Id})", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process started (PID: {process.Id}). Waiting for exit...");

                await process.WaitForExitAsync(cancellationToken); // Wait for SteamCMD to finish
                result.ExitCode = process.ExitCode;

                _logger.LogInfo($"SteamCMD process exited with code: {process.ExitCode}", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process exited with code: {process.ExitCode}.");

                // Check cancellation *after* process exit but *before* parsing/processing
                cancellationToken.ThrowIfCancellationRequested();

                // --- 3. Parse Workshop Log File ---
                bool workshopLogParsed = false;
                int processedLogEntryCount = 0;

                _logger.LogInfo($"Attempting to parse workshop log ({workshopLogPath}) using filter time >= {filterTime:O}", "SteamCmdDownloader");
                result.LogMessages.Add($"Attempting to parse workshop log using filter time >= {filterTime:O}");

                if (File.Exists(workshopLogPath))
                {
                    try
                    {
                        var logLines = await File.ReadAllLinesAsync(workshopLogPath, cancellationToken);
                        _logger.LogDebug($"Found workshop log with {logLines.Length} lines", "SteamCmdDownloader");
                        result.LogMessages.Add($"Found workshop log with {logLines.Length} lines");

                        // Add log sample (last 50 lines)
                        var logSample = logLines.TakeLast(Math.Min(50, logLines.Length));
                        result.LogMessages.Add("=== WORKSHOP LOG SAMPLE (last 50 lines) ===");
                        result.LogMessages.AddRange(logSample);
                        result.LogMessages.Add("=== END OF WORKSHOP LOG SAMPLE ===");

                        foreach (var line in logLines)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool matched = false;

                            // Try matching success pattern
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
                                        // Store or update the result, keeping the latest timestamp
                                        if (!sessionResults.TryGetValue(id, out var existing) || entryTime > existing.Timestamp)
                                        {
                                            sessionResults[id] = (Success: true, Timestamp: entryTime, Reason: null);
                                        }
                                        matched = true;
                                    }
                                } else { _logger.LogWarning($"Could not parse timestamp '{timestampStr}' in workshop log line: {line}", "SteamCmdDownloader"); }
                            }

                            // If not success, try matching failure pattern
                            if (!matched)
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
                                             // Store or update the result, keeping the latest timestamp
                                             if (!sessionResults.TryGetValue(id, out var existing) || entryTime > existing.Timestamp)
                                             {
                                                 sessionResults[id] = (Success: false, Timestamp: entryTime, Reason: reason);
                                             }
                                            matched = true;
                                        }
                                    } else { _logger.LogWarning($"Could not parse timestamp '{timestampStr}' in workshop log line: {line}", "SteamCmdDownloader"); }
                                }
                            }
                        } // End foreach log line

                        workshopLogParsed = true;
                        _logger.LogInfo($"Workshop log parsing complete. Processed {processedLogEntryCount} relevant entries for this session.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Workshop log parsing complete. Processed {processedLogEntryCount} relevant entries.");
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogError($"Error reading workshop log file {workshopLogPath}: {ioEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error reading workshop log file: {ioEx.Message}");
                        // workshopLogParsed remains false
                    }
                    catch (Exception ex)
                    {
                         _logger.LogError($"Unexpected error parsing workshop log file {workshopLogPath}: {ex.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Unexpected error parsing workshop log file: {ex.Message}");
                        // workshopLogParsed remains false
                    }
                }
                else
                {
                     _logger.LogWarning($"Warning: Workshop log file not found at {workshopLogPath}.", "SteamCmdDownloader");
                     result.LogMessages.Add($"Warning: Workshop log file not found at {workshopLogPath}. Cannot confirm item status reliably.");
                     // workshopLogParsed remains false
                }


                // --- 4. Determine Provisional Success/Failure from Logs ---
                var provisionallySucceededIds = new HashSet<string>();
                var initiallyFailedIds = new HashSet<string>(); // Items that failed according to logs or ambiguity

                if (!workshopLogParsed && requestedIds.Any()) // Only fail all if log missing AND we expected results
                {
                    _logger.LogError($"Workshop log unusable. Assuming all requested items failed initial download check.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Workshop log unusable. Assuming all items failed initial download check.");
                    initiallyFailedIds.UnionWith(requestedIds);
                }
                else
                {
                    // Use parsed sessionResults
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
                             string reason = sessionResults.TryGetValue(requestedId, out var failResult) ? (failResult.Reason ?? "Unknown") : "No result found";
                             DateTime? failTime = sessionResults.TryGetValue(requestedId, out failResult) ? failResult.Timestamp : (DateTime?)null;
                             _logger.LogWarning($"Item {requestedId} status from log: FAILED (Reason: {reason}, Log entry at {failTime?.ToString("O") ?? "N/A"})", "SteamCmdDownloader");
                             result.LogMessages.Add($"Item {requestedId} failed download or no log entry found this session.");
                        }
                    }
                }
                 // Add all initially failed items to the final failed list now
                finalFailedItems.AddRange(initiallyFailedIds.Select(id => itemLookup[id]));

                // --- 5. Process Provisionally Succeeded Items (Timestamp, Delete, Move/Copy) ---
                _logger.LogInfo($"Processing {provisionallySucceededIds.Count} items marked as successful by logs...", "SteamCmdDownloader");
                result.LogMessages.Add($"Processing {provisionallySucceededIds.Count} potentially successful items...");

                string steamCmdWorkshopBaseDir = _pathService.SteamCmdWorkshopContentPath; // Base: ...\SteamCMD_Data\steam\steamapps\workshop\content\294100

                foreach (var successId in provisionallySucceededIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DownloadItem currentItem = itemLookup[successId]; // Should exist
                    string sourcePath = Path.Combine(steamCmdWorkshopBaseDir, successId);
                    string targetPath = Path.Combine(userModsPath, successId);
                    bool itemProcessedSuccessfully = false;

                    _logger.LogInfo($"Processing item {successId} ({currentItem.Name}): Source='{sourcePath}', Target='{targetPath}'", "SteamCmdDownloader");

                    // CRITICAL CHECK: Does the downloaded directory actually exist?
                    if (!Directory.Exists(sourcePath))
                    {
                        _logger.LogError($"Item {successId}: Directory '{sourcePath}' not found despite log saying OK. Marking as failed.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error: Item {successId} ({currentItem.Name}) downloaded files not found at expected location. Download failed.");
                        finalFailedItems.Add(currentItem); // Add to failed list
                        continue; // Skip to next item
                    }

                    try
                    {
                        // Step 5a: Create Timestamps in Source Path
                        _logger.LogDebug($"Item {successId}: Creating timestamp files in source path '{sourcePath}'", "SteamCmdDownloader");
                        string pubDate = currentItem.PublishDate ?? string.Empty;
                        string stdDate = currentItem.StandardDate ?? string.Empty;
                        // ModService will throw if dates are invalid now
                        await _modService.CreateTimestampFilesAsync(sourcePath, successId, pubDate, stdDate);
                        _logger.LogInfo($"Item {successId}: Timestamp files created successfully in source.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Item {successId}: Timestamp files created.");

                        cancellationToken.ThrowIfCancellationRequested();

                        // Step 5b: Delete Existing Mod at Target Path (if exists)
                        if (Directory.Exists(targetPath))
                        {
                            _logger.LogInfo($"Item {successId}: Deleting existing directory at target path '{targetPath}'", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {successId}: Removing existing version...");
                            Directory.Delete(targetPath, true); // Recursive delete
                            await Task.Delay(50, cancellationToken); // Small delay for FS
                            if (Directory.Exists(targetPath)) // Verify deletion
                            {
                                throw new IOException($"Failed to delete existing directory after attempt: {targetPath}");
                            }
                             _logger.LogInfo($"Item {successId}: Existing directory deleted successfully.", "SteamCmdDownloader");
                        } else {
                            _logger.LogDebug($"Item {successId}: No existing directory found at target path '{targetPath}'.", "SteamCmdDownloader");
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        // Step 5c: Move or Copy Mod from Source to Target Path
                        string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
                        string? targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));
                        bool crossVolume = sourceRoot != null && targetRoot != null &&
                                           !string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);

                        if (crossVolume)
                        {
                             _logger.LogInfo($"Item {successId}: Performing cross-volume copy from '{sourcePath}' to '{targetPath}'", "SteamCmdDownloader");
                             result.LogMessages.Add($"Item {successId}: Copying new version into mods folder (cross-volume)...");
                             await CopyDirectoryRecursivelyAsync(sourcePath, targetPath, cancellationToken); // Use helper
                             _logger.LogInfo($"Item {successId}: Cross-volume copy complete. Deleting source.", "SteamCmdDownloader");
                             Directory.Delete(sourcePath, true); // Delete original after successful copy
                        }
                        else
                        {
                            _logger.LogInfo($"Item {successId}: Moving directory from source '{sourcePath}' to target '{targetPath}' (same volume)", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {successId}: Moving new version into mods folder...");
                            Directory.Move(sourcePath, targetPath);
                        }

                        await Task.Delay(50, cancellationToken); // Brief pause for FS changes to settle

                        // Verify outcome: Target must exist, Source must NOT exist anymore
                        if (!Directory.Exists(targetPath) || Directory.Exists(sourcePath))
                        {
                             throw new IOException($"Failed to verify move/copy operation. Target exists: {Directory.Exists(targetPath)}, Source exists: {Directory.Exists(sourcePath)}");
                        }

                        _logger.LogInfo($"Item {successId}: Directory moved/copied successfully.", "SteamCmdDownloader");
                        itemProcessedSuccessfully = true;
                    }
                    catch (OperationCanceledException) { throw; } // Re-throw cancellation
                    catch (Exception ex)
                    {
                        _logger.LogError($"Item {successId}: FAILED post-download processing: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error processing item {successId} ({currentItem.Name}): {ex.Message}. Downloaded files may remain in SteamCMD data folder.");
                         // Ensure it's marked as failed
                         finalFailedItems.Add(currentItem);
                         itemProcessedSuccessfully = false;
                         // DO NOT delete sourcePath here - leave it for diagnosis if copy/move failed
                    }

                    if (itemProcessedSuccessfully)
                    {
                        finalSucceededItems.Add(currentItem);
                         result.LogMessages.Add($"Item {successId} ({currentItem.Name}) successfully downloaded and processed.");
                    }
                } // End foreach provisionallySucceededId


                // --- 6. Populate Final Result Object ---
                result.SucceededItems.AddRange(finalSucceededItems);
                // Failed items were added either from initial log failure or post-processing failure
                // Use the existing list modification logic to ensure distinct items
                var distinctFailedItems = finalFailedItems.Except(result.SucceededItems).Distinct().ToList();
                result.FailedItems.Clear();
                result.FailedItems.AddRange(distinctFailedItems);

                _logger.LogInfo($"Final results: Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
                 if (result.SucceededItems.Any()) _logger.LogInfo($"Succeeded IDs: {string.Join(", ", result.SucceededItems.Select(i => i.SteamId))}", "SteamCmdDownloader");
                 if (result.FailedItems.Any()) _logger.LogWarning($"Failed IDs: {string.Join(", ", result.FailedItems.Select(i => i.SteamId))}", "SteamCmdDownloader");

                // --- 7. Define Overall Success ---
                // Requires exit code 0 AND no items ended up in the failed list AND we requested items initially.
                result.OverallSuccess = (process.ExitCode == 0 && !result.FailedItems.Any() && requestedIds.Any());

                // Provide clearer final status message
                if (result.OverallSuccess) // && result.SucceededItems.Any() implicitly true if requestedIds.Any() and no failures
                {
                    _logger.LogInfo("Download operation completed successfully for all requested items.", "SteamCmdDownloader");
                    result.LogMessages.Add("Download completed successfully for all items.");
                }
                 else if (process.ExitCode == 0 && !result.FailedItems.Any() && !requestedIds.Any()) // No items requested case
                {
                     _logger.LogInfo("Download operation finished. No items were requested.", "SteamCmdDownloader");
                     result.LogMessages.Add("Download finished. No items requested.");
                     result.OverallSuccess = true; // Considered success if no work requested
                 }
                else if (process.ExitCode != 0)
                {
                     _logger.LogError($"Download operation failed. SteamCMD process reported an error (ExitCode: {process.ExitCode}). Check logs.", "SteamCmdDownloader");
                     result.LogMessages.Add($"Download operation failed. SteamCMD process error (ExitCode: {process.ExitCode}).");
                }
                 else if (!workshopLogParsed && requestedIds.Any())
                {
                     _logger.LogError($"Download operation failed. Could not read/parse the workshop log file ({workshopLogPath}) to confirm status.", "SteamCmdDownloader");
                     // Log message already added during parsing attempt
                }
                 else if (result.FailedItems.Any())
                {
                    _logger.LogWarning($"Download operation completed with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Download completed with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}. Check messages for details.");
                }
                else // Should not be reached if logic above is correct, but as fallback:
                {
                     _logger.LogWarning($"Download operation finished with unclear status. ExitCode: {process.ExitCode}, Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
                     result.LogMessages.Add($"Download finished with unclear status.");
                }

                _logger.LogInfo($"Download processing finished. Overall success flag: {result.OverallSuccess}", "SteamCmdDownloader");

            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download operation cancelled by user", "SteamCmdDownloader");
                result.LogMessages.Add("Download operation cancelled.");
                result.OverallSuccess = false;

                // Assign remaining requested items as failed if not already successfully processed
                var succeededSoFarIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet(); // Use final processed list
                finalFailedItems.AddRange(validItems.Where(i => i.SteamId != null && !succeededSoFarIds.Contains(i.SteamId)));

                // Update the existing result lists correctly
                result.SucceededItems.Clear();
                result.SucceededItems.AddRange(finalSucceededItems);

                var distinctFailed = finalFailedItems.Distinct().ToList(); // Ensure distinct
                result.FailedItems.Clear();
                result.FailedItems.AddRange(distinctFailed);

                return result; // Return partially processed result on cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download failed with unexpected exception: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
                result.LogMessages.Add($"Download failed with unexpected exception: {ex.Message}");
                result.OverallSuccess = false;

                // Ensure all items not successfully processed are marked failed
                var succeededIds = finalSucceededItems.Select(i => i.SteamId).ToHashSet(); // Use final processed list
                finalFailedItems.AddRange(validItems.Where(i => i.SteamId != null && !succeededIds.Contains(i.SteamId)));

                // Update existing result lists correctly
                result.SucceededItems.Clear();
                result.SucceededItems.AddRange(finalSucceededItems); // Ensure result reflects final state

                var distinctFailed = finalFailedItems.Distinct().ToList(); // Ensure distinct failed items
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
                    catch (Exception ex) { _logger.LogWarning($"Failed to clean up script file: {ex.Message}", "SteamCmdDownloader"); }
                }
                 // Keep log files for diagnosis
                if (File.Exists(primaryLogPath)) { _logger.LogInfo($"Primary log file kept at: {primaryLogPath}", "SteamCmdDownloader"); result.LogMessages.Add($"Primary log file kept at: {primaryLogPath}"); }
                if (File.Exists(workshopLogPath)) { _logger.LogInfo($"Workshop log file kept at: {workshopLogPath}", "SteamCmdDownloader"); result.LogMessages.Add($"Workshop log file kept at: {workshopLogPath}"); }
            }

            return result;
        }

                /// <summary>
        /// Recursively copies a directory and its contents.
        /// </summary>
        /// <param name="sourceDir">The source directory path.</param>
        /// <param name="destinationDir">The destination directory path.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task CopyDirectoryRecursivelyAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDir);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceDir}");
            }

            // If the destination directory doesn't exist, create it.
            Directory.CreateDirectory(destinationDir);
            cancellationToken.ThrowIfCancellationRequested();

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, file.Name);
                 // Use CopyToAsync for potential UI responsiveness on large files if needed,
                 // but File.Copy is often sufficient here. Using simple copy for now.
                 // await Task.Run(() => file.CopyTo(tempPath, true), cancellationToken); // Copy with overwrite
                 file.CopyTo(tempPath, true); // Simple copy with overwrite
                 await Task.Yield(); // Allow other tasks to run briefly
            }

            // Get subdirectories and copy them recursively
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
