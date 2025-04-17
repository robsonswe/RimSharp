using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization; // Needed for CultureInfo and DateTimeStyles
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop
{
    public class SteamCmdDownloader : ISteamCmdDownloader
    {
        private const string RimworldAppId = "294100";

        // Regexes specifically for logs/workshop_log.txt
        // Now include timestamp capture group (Group 1)
        private static readonly Regex WorkshopLogSuccessRegex = new Regex(
             @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*OK",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WorkshopLogFailureRegex = new Regex(
            @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*(?!OK\s*$)(\w+[\w\s]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Group 1: Timestamp (YYYY-MM-DD HH:MM:SS)
        // Group 2: Item ID
        // Group 3 (Failure only): Reason

        // Regex for generic errors in the primary log
        private static readonly Regex GenericErrorRegex = new Regex(
            @"^ERROR! (.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;

        // Timestamp format used in workshop_log.txt
        private const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss";

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            IDialogService dialogService,
            ILoggerService logger)
        {
            _pathService = pathService;
            _installer = installer;
            _dialogService = dialogService;
            _logger = logger;
        }

        public async Task<SteamCmdDownloadResult> DownloadModsAsync(
                IEnumerable<DownloadItem> itemsToDownload,
                bool validate,
                CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("Starting SteamCMD download operation", "SteamCmdDownloader");
            var result = new SteamCmdDownloadResult();
            bool isSetupComplete = await _installer.CheckSetupAsync();

            if (!isSetupComplete || string.IsNullOrEmpty(_pathService.SteamCmdExePath))
            {
                _logger.LogError("Download failed: SteamCMD is not set up properly", "SteamCmdDownloader");
                result.LogMessages.Add("Download failed: SteamCMD is not set up.");
                result.OverallSuccess = false;
                return result;
            }

            var validItems = itemsToDownload
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.SteamId) && long.TryParse(i.SteamId.Trim(), out _))
                .ToList();

            if (!validItems.Any())
            {
                _logger.LogInfo("Download skipped: No valid workshop items provided", "SteamCmdDownloader");
                result.LogMessages.Add("Download skipped: No valid workshop items provided.");
                result.OverallSuccess = true;
                return result;
            }

            foreach (var item in validItems)
            {
                _logger.LogDebug($"Preparing to download item: {item.SteamId} - {item.Name}", "SteamCmdDownloader");
            }

            var itemLookup = validItems.ToDictionary(i => i.SteamId);
            var requestedIds = itemLookup.Keys.ToHashSet();
            // Use dictionaries to store the *latest* result per ID for this run
            var sessionResults = new Dictionary<string, (bool Success, DateTime Timestamp, string? Reason)>();

            string? scriptDir = Path.GetDirectoryName(_pathService.SteamCmdExePath);
            if (string.IsNullOrEmpty(scriptDir))
            {
                _logger.LogError("Error: Cannot determine SteamCMD directory", "SteamCmdDownloader");
                result.LogMessages.Add("Error: Cannot determine SteamCMD directory.");
                result.OverallSuccess = false;
                return result;
            }

            string scriptId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string scriptPath = Path.Combine(scriptDir, $"rimsharp_dl_script_{scriptId}.txt");
            string primaryLogPath = Path.Combine(scriptDir, $"rimsharp_dl_log_{scriptId}.log");
            string workshopLogPath = Path.Combine(_pathService.SteamCmdInstallPath, "logs", "workshop_log.txt");

            _logger.LogDebug($"Script path: {scriptPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Primary log path (+log_file): {primaryLogPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Workshop log path (detailed status): {workshopLogPath}", "SteamCmdDownloader");

            // --- Record Start Time ---
            DateTime startTime = DateTime.Now;
            // Use a slightly earlier time for filtering to catch logs that might appear *just* before WaitForExit returns
            DateTime filterTime = startTime.AddSeconds(-5);
            _logger.LogInfo($"Operation started at {startTime:O}. Filtering log entries >= {filterTime:O}", "SteamCmdDownloader");


            try
            {
                // --- 1. Create Script ---
                // (Script creation remains the same)
                _logger.LogInfo("Generating SteamCMD script...", "SteamCmdDownloader");
                result.LogMessages.Add("Generating SteamCMD script...");
                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine($"force_install_dir \"{_pathService.SteamCmdSteamAppsPath}\"");
                scriptBuilder.AppendLine("login anonymous");
                string downloadCommand = $"workshop_download_item {RimworldAppId}";
                string validateSuffix = validate ? " validate" : "";
                foreach (var item in validItems)
                {
                    scriptBuilder.AppendLine($"{downloadCommand} {item.SteamId.Trim()}{validateSuffix}");
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
                if (File.Exists(primaryLogPath)) { try { File.Delete(primaryLogPath); } catch { /* Ignore */ } }

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _pathService.SteamCmdExePath,
                    Arguments = $"+runscript \"{scriptPath}\" +log_file \"{primaryLogPath}\"",
                    UseShellExecute = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false,
                    WorkingDirectory = scriptDir,
                };

                using var process = new Process { StartInfo = processStartInfo };

                process.Start();
                _logger.LogInfo($"SteamCMD process started (PID: {process.Id})", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process started (PID: {process.Id}). Waiting for exit...");

                await process.WaitForExitAsync(cancellationToken);
                result.ExitCode = process.ExitCode;

                _logger.LogInfo($"SteamCMD process exited with code: {process.ExitCode}", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process exited with code: {process.ExitCode}.");

                // --- 3. Parse Workshop Log File with Timestamp Filtering ---
                bool workshopLogParsed = false;
                int processedLogEntryCount = 0;

                _logger.LogInfo($"Attempting to parse workshop log ({workshopLogPath}) using filter time >= {filterTime:O}", "SteamCmdDownloader");
                result.LogMessages.Add($"Attempting to parse workshop log using filter time >= {filterTime:O}");

                if (File.Exists(workshopLogPath))
                {
                    try
                    {
                        // Read all lines - necessary if multiple items download in one run
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
                        }
                        workshopLogParsed = true;
                        _logger.LogInfo($"Workshop log parsing complete. Processed {processedLogEntryCount} relevant entries for this session.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Workshop log parsing complete. Processed {processedLogEntryCount} relevant entries for this session.");

                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogError($"Error reading workshop log file {workshopLogPath}: {ioEx.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Error reading workshop log file: {ioEx.Message}");
                    }
                    catch (Exception ex)
                    {
                         _logger.LogError($"Unexpected error parsing workshop log file {workshopLogPath}: {ex.Message}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Unexpected error parsing workshop log file: {ex.Message}");
                    }
                }
                else
                {
                     _logger.LogWarning($"Warning: Workshop log file not found at {workshopLogPath}.", "SteamCmdDownloader");
                     result.LogMessages.Add($"Warning: Workshop log file not found at {workshopLogPath}. Cannot confirm item status reliably.");
                }

                // --- 4. Determine Final Item Status from Session Results or Exit Code ---
                var succeededIds = new HashSet<string>();
                var failedIds = new HashSet<string>();

                if (!workshopLogParsed)
                {
                    // Workshop log unusable, rely solely on exit code
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"Workshop log unusable and exit code ({process.ExitCode}) indicates failure. Marking all requested items as failed.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Workshop log unusable and exit code ({process.ExitCode}) indicates failure. Assuming all items failed.");
                        failedIds.UnionWith(requestedIds);
                    }
                    else
                    {
                        _logger.LogWarning($"Workshop log unusable, but exit code is 0. Marking all requested items as failed due to ambiguity.", "SteamCmdDownloader");
                        result.LogMessages.Add($"Warning: Workshop log unusable, but exit code is 0. Assuming all items failed due to lack of confirmation.");
                        failedIds.UnionWith(requestedIds);
                    }
                }
                else
                {
                    // Workshop log was parsed, use sessionResults
                    foreach (var requestedId in requestedIds)
                    {
                        if (sessionResults.TryGetValue(requestedId, out var latestResult))
                        {
                            if (latestResult.Success)
                            {
                                succeededIds.Add(requestedId);
                                _logger.LogInfo($"Item {requestedId} final status: SUCCEEDED (Log entry at {latestResult.Timestamp:O})", "SteamCmdDownloader");
                            }
                            else
                            {
                                failedIds.Add(requestedId);
                                _logger.LogWarning($"Item {requestedId} final status: FAILED (Reason: {latestResult.Reason ?? "Unknown"}, Log entry at {latestResult.Timestamp:O})", "SteamCmdDownloader");
                            }
                        }
                        else
                        {
                            // No entry found for this item in the relevant time frame
                            failedIds.Add(requestedId);
                             _logger.LogWarning($"Item {requestedId} final status: FAILED (No result found in workshop log for this session)", "SteamCmdDownloader");
                             result.LogMessages.Add($"Warning: Item {requestedId} had no result entry in the workshop log for this session.");
                        }
                    }
                }

                // --- 5. Populate Result Object ---
                result.SucceededItems.AddRange(succeededIds.Select(id => itemLookup[id]));
                result.FailedItems.AddRange(failedIds.Select(id => itemLookup[id]));

                _logger.LogInfo($"Final item status determined. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}", "SteamCmdDownloader");
                if (result.SucceededItems.Any()) _logger.LogInfo($"Succeeded IDs: {string.Join(", ", result.SucceededItems.Select(i => i.SteamId))}", "SteamCmdDownloader");
                if (result.FailedItems.Any()) _logger.LogWarning($"Failed IDs: {string.Join(", ", result.FailedItems.Select(i => i.SteamId))}", "SteamCmdDownloader");


                // --- 6. Define Overall Success ---
                // Still requires exit code 0 AND all requested items succeeded based on *this session's* log entries.
                bool allRequestedItemsSucceededThisSession = succeededIds.Count == requestedIds.Count;
                result.OverallSuccess = (process.ExitCode == 0 && failedIds.Count == 0 && allRequestedItemsSucceededThisSession);

                // Provide clearer final status message
                if (result.OverallSuccess)
                {
                    _logger.LogInfo("Download operation completed successfully for all items.", "SteamCmdDownloader");
                    result.LogMessages.Add("Download completed successfully for all items.");
                }
                else if (process.ExitCode != 0)
                {
                     _logger.LogError($"Download operation failed. SteamCMD process reported an error (ExitCode: {process.ExitCode}). Check logs for details.", "SteamCmdDownloader");
                     result.LogMessages.Add($"Download operation failed. SteamCMD process error (ExitCode: {process.ExitCode}).");
                }
                else if (!workshopLogParsed)
                {
                     _logger.LogError($"Download operation failed. Could not read/parse the workshop log file ({workshopLogPath}) to confirm status.", "SteamCmdDownloader");
                     result.LogMessages.Add($"Download operation failed. Cannot confirm status from workshop log file.");
                }
                 else if (result.FailedItems.Any())
                {
                    _logger.LogWarning($"Download operation completed with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}.", "SteamCmdDownloader");
                    result.LogMessages.Add($"Download completed with failures. Succeeded: {result.SucceededItems.Count}, Failed: {result.FailedItems.Count}.");
                }
                else // Exit code 0, log parsed, but not all items succeeded (e.g., undetermined marked as failed)
                {
                     _logger.LogWarning($"Download operation finished, but not all items were confirmed successful in the workshop log for this session. Succeeded: {result.SucceededItems.Count}, Total Requested: {requestedIds.Count}", "SteamCmdDownloader");
                     result.LogMessages.Add($"Download finished, but not all items confirmed successful this session. Succeeded: {result.SucceededItems.Count}/{requestedIds.Count}.");
                }

                _logger.LogInfo($"Download processing finished. Overall success flag: {result.OverallSuccess}", "SteamCmdDownloader");

            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download operation cancelled by user", "SteamCmdDownloader");
                result.LogMessages.Add("Download operation cancelled.");
                result.OverallSuccess = false;
                // Assign remaining items based on potentially partial sessionResults if available, otherwise mark as failed.
                var determinedIds = sessionResults.Keys.ToHashSet();
                result.FailedItems.AddRange(validItems.Where(i => !determinedIds.Contains(i.SteamId)));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download failed with unexpected exception: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
                result.LogMessages.Add($"Download failed with unexpected exception: {ex.Message}");
                result.OverallSuccess = false;
                result.FailedItems.AddRange(validItems.Where(i => !result.SucceededItems.Contains(i) && !result.FailedItems.Contains(i)));
                _dialogService.ShowError("Download Failed", $"An unexpected error occurred while running SteamCMD:\n\n{ex.Message}");
            }
            finally
            {
                // Clean up script file
                if (File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); _logger.LogDebug($"Cleaned up script file: {scriptPath}", "SteamCmdDownloader"); }
                    catch (Exception ex) { _logger.LogWarning($"Failed to clean up script file: {ex.Message}", "SteamCmdDownloader"); }
                }
                 // Keep log files
                if (File.Exists(primaryLogPath)) { _logger.LogInfo($"Primary log file kept at: {primaryLogPath}", "SteamCmdDownloader"); result.LogMessages.Add($"Primary log file kept at: {primaryLogPath}"); }
                if (File.Exists(workshopLogPath)) { _logger.LogInfo($"Workshop log file kept at: {workshopLogPath}", "SteamCmdDownloader"); result.LogMessages.Add($"Workshop log file kept at: {workshopLogPath}"); }
            }

            return result;
        }
    }
}