using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // Keep Regexes for log parsing - improved for greater flexibility
        private static readonly Regex DownloadSuccessRegex = new Regex(
            @"Success\.\s*Downloaded item (\d+) to ""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DownloadFailureRegex = new Regex(
            @"ERROR! (Failed to download item|Timeout downloading item) (\d+)\.",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GenericErrorRegex = new Regex(
            @"^ERROR! (.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService;
        private readonly ILoggerService _logger;

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
                result.OverallSuccess = true; // Nothing to do is success
                return result;
            }

            // Log items to be downloaded
            foreach (var item in validItems)
            {
                _logger.LogDebug($"Preparing to download item: {item.SteamId} - {item.Name}", "SteamCmdDownloader");
            }

            // Create a lookup for easier matching later
            var itemLookup = validItems.ToDictionary(i => i.SteamId);
            var requestedIds = itemLookup.Keys.ToHashSet();
            var succeededIds = new HashSet<string>();
            var failedIds = new HashSet<string>();

            string scriptDir = Path.GetDirectoryName(_pathService.SteamCmdExePath);
            if (string.IsNullOrEmpty(scriptDir))
            {
                _logger.LogError("Error: Cannot determine SteamCMD directory", "SteamCmdDownloader");
                result.LogMessages.Add("Error: Cannot determine SteamCMD directory.");
                result.OverallSuccess = false;
                return result;
            }

            string scriptId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string scriptPath = Path.Combine(scriptDir, $"rimsharp_dl_script_{scriptId}.txt");
            string logPath = Path.Combine(scriptDir, $"rimsharp_dl_log_{scriptId}.log");

            _logger.LogDebug($"Script path: {scriptPath}", "SteamCmdDownloader");
            _logger.LogDebug($"Log path: {logPath}", "SteamCmdDownloader");

            try
            {
                // --- 1. Create Script ---
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
                result.LogMessages.Add($"Log will be at: {logPath}");

                cancellationToken.ThrowIfCancellationRequested();

                // --- 2. Execute SteamCMD (Show Window) ---
                _logger.LogInfo($"Executing SteamCMD...", "SteamCmdDownloader");
                result.LogMessages.Add($"Executing SteamCMD...");
                if (File.Exists(logPath)) File.Delete(logPath); // Ensure clean log

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _pathService.SteamCmdExePath,
                    Arguments = $"+runscript \"{scriptPath}\" +log_file \"{logPath}\"", // Force logging via argument
                    UseShellExecute = true, // <<< SHOW WINDOW
                    RedirectStandardOutput = false, // <<< Cannot redirect if UseShellExecute is true
                    RedirectStandardError = false, // <<< Cannot redirect
                    CreateNoWindow = false, // <<< Show the window
                    WorkingDirectory = scriptDir,
                };

                using var process = new Process { StartInfo = processStartInfo };

                process.Start();
                _logger.LogInfo($"SteamCMD process started (PID: {process.Id})", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process started (PID: {process.Id}). Waiting for exit...");

                // Wait for the process to exit, respecting cancellation
                await process.WaitForExitAsync(cancellationToken);
                result.ExitCode = process.ExitCode;

                _logger.LogInfo($"SteamCMD process exited with code: {process.ExitCode}", "SteamCmdDownloader");
                result.LogMessages.Add($"SteamCMD process exited with code: {process.ExitCode}.");

                // --- 3. Parse Log File for Results ---
                _logger.LogInfo($"Parsing log file: {logPath}", "SteamCmdDownloader");
                result.LogMessages.Add($"Parsing log file: {logPath}");

                if (File.Exists(logPath))
                {
                    var logLines = await File.ReadAllLinesAsync(logPath, cancellationToken);
                    _logger.LogDebug($"Found log file with {logLines.Length} lines", "SteamCmdDownloader");
                    result.LogMessages.Add($"Found log file with {logLines.Length} lines");

                    // Add first 20 lines of log to results for debugging
                    var logSample = logLines.Take(Math.Min(20, logLines.Length));
                    result.LogMessages.Add("=== LOG FILE SAMPLE (first 20 lines) ===");
                    result.LogMessages.AddRange(logSample);
                    result.LogMessages.Add("=== END OF LOG SAMPLE ===");

                    // Also log the full log file at debug level
                    _logger.LogDebug($"Full log content:\n{string.Join("\n", logLines)}", "SteamCmdDownloader");

                    int successMatches = 0;
                    int failureMatches = 0;

                    foreach (var line in logLines)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // First try the precise regex patterns
                        Match successMatch = DownloadSuccessRegex.Match(line);
                        if (successMatch.Success)
                        {
                            string id = successMatch.Groups[1].Value;
                            string path = successMatch.Groups[2].Value;
                            _logger.LogDebug($"SUCCESS MATCH: Found ID {id} downloaded to {path}", "SteamCmdDownloader");

                            if (requestedIds.Contains(id))
                            {
                                succeededIds.Add(id);
                                failedIds.Remove(id); // Ensure it's not marked as failed if it succeeded later
                                _logger.LogInfo($"Marked item {id} as SUCCEEDED", "SteamCmdDownloader");
                                successMatches++;
                            }
                            continue; // Move to next line
                        }

                        // Fallback parsing for success if regex doesn't match
                        if (line.Contains("Success") && line.Contains("Downloaded item"))
                        {
                            _logger.LogDebug($"Found success text but regex didn't match: {line}", "SteamCmdDownloader");

                            // Try a simpler extraction method if the regex failed
                            var parts = line.Split(new[] { "Downloaded item ", " to " }, StringSplitOptions.None);
                            if (parts.Length >= 3)
                            {
                                string id = parts[1].Trim();
                                if (requestedIds.Contains(id))
                                {
                                    succeededIds.Add(id);
                                    failedIds.Remove(id);
                                    _logger.LogInfo($"Marked item {id} as SUCCEEDED using fallback parsing", "SteamCmdDownloader");
                                    successMatches++;
                                }
                            }
                        }

                        Match failureMatch = DownloadFailureRegex.Match(line);
                        if (failureMatch.Success)
                        {
                            string id = failureMatch.Groups[2].Value;
                            _logger.LogDebug($"FAILURE MATCH: Found failed ID {id}", "SteamCmdDownloader");

                            if (requestedIds.Contains(id) && !succeededIds.Contains(id)) // Only mark as failed if not already succeeded
                            {
                                failedIds.Add(id);
                                _logger.LogWarning($"Marked item {id} as FAILED", "SteamCmdDownloader");
                                failureMatches++;
                            }
                            continue; // Move to next line
                        }

                        // Log generic errors too
                        Match errorMatch = GenericErrorRegex.Match(line);
                        if (errorMatch.Success)
                        {
                            string errorMsg = errorMatch.Groups[1].Value;
                            _logger.LogWarning($"Detected generic error in log: {errorMsg}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Detected generic error: {errorMsg}");
                        }
                    }

                    _logger.LogInfo($"Log parsing complete. Success matches: {successMatches}, Failure matches: {failureMatches}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Log parsing complete. Success matches: {successMatches}, Failure matches: {failureMatches}");
                    _logger.LogInfo($"Succeeded IDs: {succeededIds.Count}, Failed IDs: {failedIds.Count}", "SteamCmdDownloader");

                    // Extra diagnostic - list the IDs that succeeded
                    if (succeededIds.Count > 0)
                    {
                        string successList = string.Join(", ", succeededIds);
                        _logger.LogInfo($"Successfully downloaded IDs: {successList}", "SteamCmdDownloader");
                        result.LogMessages.Add($"Successfully downloaded IDs: {successList}");
                    }
                }
                else
                {
                    _logger.LogWarning($"Warning: Log file not found at {logPath} after execution", "SteamCmdDownloader");
                    result.LogMessages.Add($"Warning: Log file not found at {logPath} after execution.");

                    // Check if SteamCMD created logs elsewhere
                    string steamCmdLogsDir = Path.Combine(scriptDir, "logs");
                    if (Directory.Exists(steamCmdLogsDir))
                    {
                        var steamLogFiles = Directory.GetFiles(steamCmdLogsDir, "*.txt").OrderByDescending(f => new FileInfo(f).LastWriteTime).Take(3).ToList();
                        if (steamLogFiles.Any())
                        {
                            _logger.LogInfo($"Found SteamCMD log files in logs directory: {string.Join(", ", steamLogFiles.Select(Path.GetFileName))}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Found SteamCMD log files in logs directory: {string.Join(", ", steamLogFiles.Select(Path.GetFileName))}");

                            // Try to parse the most recent log file
                            var newestLog = steamLogFiles.First();
                            try
                            {
                                var alternateLogLines = await File.ReadAllLinesAsync(newestLog, cancellationToken);
                                result.LogMessages.Add($"Reading alternate log file: {newestLog} ({alternateLogLines.Length} lines)");
                                _logger.LogDebug($"Alternative log content:\n{string.Join("\n", alternateLogLines)}", "SteamCmdDownloader");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Failed to read alternate log file: {ex.Message}", "SteamCmdDownloader");
                            }
                        }
                    }
                }

                // --- 4. Populate Result Object ---
                foreach (var id in succeededIds)
                {
                    if (itemLookup.TryGetValue(id, out var item))
                    {
                        result.SucceededItems.Add(item);
                    }
                }

                foreach (var id in failedIds)
                {
                    if (itemLookup.TryGetValue(id, out var item))
                    {
                        result.FailedItems.Add(item);
                    }
                }

                // Verify workshop content directories exist as a backup check
                _logger.LogInfo("Verifying workshop content directories as backup check", "SteamCmdDownloader");
                foreach (var item in validItems)
                {
                    string id = item.SteamId;
                    if (!succeededIds.Contains(id) && !failedIds.Contains(id))
                    {
                        // Check if the directory exists for this item
                        // ***** CORRECTED LINE BELOW *****
                        string expectedPath = Path.Combine(_pathService.SteamCmdWorkshopContentPath, id);
                        // ***** END OF CORRECTION *****

                        // Check for directory existence and non-emptiness (optional but safer)
                        bool directoryExists = Directory.Exists(expectedPath);
                        bool directoryNotEmpty = directoryExists && Directory.EnumerateFileSystemEntries(expectedPath).Any(); // Check if it has *any* files or subdirs

                        _logger.LogDebug($"Directory check for item {id}: {(directoryNotEmpty ? "EXISTS AND NOT EMPTY" : (directoryExists ? "EXISTS BUT EMPTY" : "MISSING"))} at {expectedPath}", "SteamCmdDownloader");

                        if (directoryNotEmpty) // Only count as success if it exists AND has content
                        {
                            // Directory exists and has content, assume success even if not parsed from logs
                            succeededIds.Add(id);
                            if (itemLookup.TryGetValue(id, out var foundItem) && !result.SucceededItems.Contains(foundItem))
                            {
                                result.SucceededItems.Add(foundItem);
                            }
                            _logger.LogInfo($"Item {id} verified as SUCCESS based on directory existence and content", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {id} verified as SUCCESS based on directory existence and content");
                        }
                        // Keep the original logic for handling failure/uncertainty based on exit code
                        else if (process.ExitCode == 0 && !directoryExists) // Directory missing but process succeeded
                        {
                            // Exit code is 0 but directory doesn't exist - uncertain state
                            // This could happen if SteamCMD *thinks* it succeeded but didn't actually download
                            // Or if the log parsing failed AND the directory check failed.
                            // Let's mark it as failed in this case for clarity, as the content isn't there.
                            failedIds.Add(id);
                            if (itemLookup.TryGetValue(id, out var foundItem) && !result.FailedItems.Contains(foundItem))
                            {
                                result.FailedItems.Add(foundItem);
                            }
                            _logger.LogWarning($"Item {id} marked as FAILED. Process exit code 0, but directory not found or empty at {expectedPath}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {id} marked as FAILED. Process exit code 0, but directory not found or empty");
                        }
                        else if (!directoryNotEmpty) // Directory missing (or empty) and exit code non-zero (or zero)
                        {
                            // Directory doesn't exist (or is empty) - treat as failure regardless of exit code if not already succeeded
                            failedIds.Add(id);
                            if (itemLookup.TryGetValue(id, out var foundItem) && !result.FailedItems.Contains(foundItem))
                            {
                                result.FailedItems.Add(foundItem);
                            }
                            _logger.LogWarning($"Item {id} marked as FAILED based on missing/empty directory at {expectedPath}", "SteamCmdDownloader");
                            result.LogMessages.Add($"Item {id} marked as FAILED based on missing/empty directory");
                        }
                    }
                }


                // Define overall success - modified logic to be more reliable
                if (process.ExitCode == 0 && result.SucceededItems.Count == validItems.Count)
                {
                    // Perfect success - all items succeeded with clean exit code
                    result.OverallSuccess = true;
                    _logger.LogInfo("Download completed successfully for all items", "SteamCmdDownloader");
                }
                else if (process.ExitCode == 0 && result.SucceededItems.Count > 0)
                {
                    // Partial success - some items succeeded with clean exit code
                    result.OverallSuccess = true;
                    _logger.LogInfo("Download completed with partial success - some items succeeded", "SteamCmdDownloader");
                }
                else if (process.ExitCode == 0)
                {
                    // Exit code is 0 but no success detected - strange case
                    // If we got here, the backup directory checks didn't find anything either
                    result.OverallSuccess = false;
                    _logger.LogWarning("Download reported success (exit code 0) but no successful downloads were detected", "SteamCmdDownloader");
                    result.LogMessages.Add("Warning: SteamCMD reported success (exit code 0) but no successful downloads were detected.");
                }
                else
                {
                    // Non-zero exit code - report as failure
                    result.OverallSuccess = false;
                    _logger.LogError($"SteamCMD process reported an error (ExitCode: {process.ExitCode})", "SteamCmdDownloader");
                    result.LogMessages.Add($"SteamCMD process reported an error (ExitCode: {process.ExitCode}).");
                }

                // Final logging
                _logger.LogInfo($"Download operation completed. Overall success: {result.OverallSuccess}", "SteamCmdDownloader");
                _logger.LogInfo($"Items succeeded: {result.SucceededItems.Count}, Items failed: {result.FailedItems.Count}", "SteamCmdDownloader");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download operation cancelled by user", "SteamCmdDownloader");
                result.LogMessages.Add("Download operation cancelled.");
                result.OverallSuccess = false;
                return result; // Return partial results if any parsing happened before cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download failed with exception: {ex.Message}\n{ex.StackTrace}", "SteamCmdDownloader");
                result.LogMessages.Add($"Download failed with exception: {ex.Message}");
                result.OverallSuccess = false;
                result.FailedItems.AddRange(validItems.Where(i => !result.SucceededItems.Contains(i))); // Assume all failed on exception
                _dialogService.ShowError("Download Failed", $"An unexpected error occurred while running SteamCMD:\n\n{ex.Message}");
            }
            finally
            {
                // Clean up script file
                if (File.Exists(scriptPath))
                {
                    try
                    {
                        File.Delete(scriptPath);
                        _logger.LogDebug($"Cleaned up script file: {scriptPath}", "SteamCmdDownloader");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Failed to clean up script file: {ex.Message}", "SteamCmdDownloader");
                    }
                }

                // Keep the log file for inspection
                if (File.Exists(logPath))
                {
                    _logger.LogInfo($"Log file kept at: {logPath}", "SteamCmdDownloader");
                    result.LogMessages.Add($"Log file kept at: {logPath}");
                }
            }

            return result;
        }
    }
}