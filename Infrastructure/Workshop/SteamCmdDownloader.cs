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
using RimSharp.Shared.Services.Contracts; // Keep for IDialogService if error dialogs needed

namespace RimSharp.Infrastructure.Workshop
{
    public class SteamCmdDownloader : ISteamCmdDownloader
    {
        private const string RimworldAppId = "294100";
        // Keep Regexes for log parsing
        private static readonly Regex DownloadSuccessRegex = new Regex(@"Success\. Downloaded item (\d+) to ""(.*?)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DownloadFailureRegex = new Regex(@"ERROR! (Failed to download item|Timeout downloading item) (\d+)\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GenericErrorRegex = new Regex(@"^ERROR! (.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase); // Catch general errors

        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService; // Keep for critical error reporting

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            IDialogService dialogService)
        {
            _pathService = pathService;
            _installer = installer;
            _dialogService = dialogService;
        }

        // <<< CHANGED Signature & Logic >>>
        public async Task<SteamCmdDownloadResult> DownloadModsAsync(
                IEnumerable<DownloadItem> itemsToDownload,
                bool validate,
                CancellationToken cancellationToken = default)
        {
            var result = new SteamCmdDownloadResult();
            bool isSetupComplete = await _installer.CheckSetupAsync();
            if (!isSetupComplete || string.IsNullOrEmpty(_pathService.SteamCmdExePath))
            {
                result.LogMessages.Add("Download failed: SteamCMD is not set up.");
                result.OverallSuccess = false;
                return result;
            }

            var validItems = itemsToDownload
                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.SteamId) && long.TryParse(i.SteamId.Trim(), out _))
                .ToList();

            if (!validItems.Any())
            {
                result.LogMessages.Add("Download skipped: No valid workshop items provided.");
                result.OverallSuccess = true; // Nothing to do is success
                return result;
            }

            // Create a lookup for easier matching later
            var itemLookup = validItems.ToDictionary(i => i.SteamId);
            var requestedIds = itemLookup.Keys.ToHashSet();
            var succeededIds = new HashSet<string>();
            var failedIds = new HashSet<string>();

            string scriptDir = Path.GetDirectoryName(_pathService.SteamCmdExePath);
            if (string.IsNullOrEmpty(scriptDir))
            {
                result.LogMessages.Add("Error: Cannot determine SteamCMD directory.");
                result.OverallSuccess = false;
                return result;
            }
            string scriptPath = Path.Combine(scriptDir, $"rimsharp_dl_script_{Guid.NewGuid()}.txt");
            string logPath = Path.Combine(scriptDir, $"rimsharp_dl_log_{Guid.NewGuid()}.log"); // Log file is crucial now

            try
            {
                // --- 1. Create Script ---
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
                result.LogMessages.Add($"SteamCMD script generated ({validItems.Count} items).");
                result.LogMessages.Add($"Log will be at: {logPath}");

                cancellationToken.ThrowIfCancellationRequested();

                // --- 2. Execute SteamCMD (Show Window) ---
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
                result.LogMessages.Add($"SteamCMD process started (PID: {process.Id}). Waiting for exit...");

                // Wait for the process to exit, respecting cancellation
                await process.WaitForExitAsync(cancellationToken);
                result.ExitCode = process.ExitCode;

                result.LogMessages.Add($"SteamCMD process exited with code: {process.ExitCode}.");

                // --- 3. Parse Log File for Results ---
                result.LogMessages.Add($"Parsing log file: {logPath}");
                if (File.Exists(logPath))
                {
                    var logLines = await File.ReadAllLinesAsync(logPath, cancellationToken);
                    result.LogMessages.AddRange(logLines); // Add all log lines to result for debugging

                    foreach (var line in logLines)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Match successMatch = DownloadSuccessRegex.Match(line);
                        if (successMatch.Success)
                        {
                            string id = successMatch.Groups[1].Value;
                            if (requestedIds.Contains(id))
                            {
                                succeededIds.Add(id);
                                failedIds.Remove(id); // Ensure it's not marked as failed if it succeeded later
                            }
                            continue; // Move to next line
                        }

                        Match failureMatch = DownloadFailureRegex.Match(line);
                        if (failureMatch.Success)
                        {
                            string id = failureMatch.Groups[2].Value;
                            if (requestedIds.Contains(id) && !succeededIds.Contains(id)) // Only mark as failed if not already succeeded
                            {
                                failedIds.Add(id);
                            }
                             continue; // Move to next line
                        }

                        // Optional: Log generic errors too
                         Match errorMatch = GenericErrorRegex.Match(line);
                        if (errorMatch.Success)
                        {
                             result.LogMessages.Add($"Detected Generic Error: {errorMatch.Groups[1].Value}");
                        }
                    }
                    result.LogMessages.Add($"Log parsing complete. Succeeded IDs: {succeededIds.Count}, Failed IDs: {failedIds.Count}");
                }
                else
                {
                    result.LogMessages.Add($"Warning: Log file not found at {logPath} after execution.");
                    // If log is missing, we can't determine individual success/failure.
                    // Assume failure based on non-zero exit code? Or let the caller decide?
                    // Let's base OverallSuccess primarily on ExitCode but report parsing failure.
                }

                // --- 4. Populate Result Object ---
                foreach (var id in succeededIds)
                {
                    if (itemLookup.TryGetValue(id, out var item))
                    {
                        result.SucceededItems.Add(item);
                    }
                }

                // Any requested item not in the succeeded list is considered failed for our purpose
                foreach (var item in validItems)
                {
                    if (!succeededIds.Contains(item.SteamId))
                    {
                        result.FailedItems.Add(item);
                    }
                }

                // Define overall success. Typically ExitCode 0 is good, but verify if parsing found expected successes.
                 result.OverallSuccess = process.ExitCode == 0;
                if (process.ExitCode == 0 && succeededIds.Count == 0 && validItems.Count > 0 && !File.Exists(logPath))
                {
                    result.LogMessages.Add("Warning: SteamCMD ExitCode 0, but log file missing or no successes parsed.");
                    // Maybe flag as not fully successful? Or rely on ExitCode? Let's stick to ExitCode for now.
                }
                 else if (process.ExitCode != 0)
                 {
                     result.LogMessages.Add($"SteamCMD process reported an error (ExitCode: {process.ExitCode}).");
                 }

            }
            catch (OperationCanceledException)
            {
                result.LogMessages.Add("Download operation cancelled.");
                result.OverallSuccess = false;
                // Attempt to kill the process if it's still running? Risky.
                // Let's assume the user closes the window or the process exited due to script 'quit'.
                return result; // Return partial results if any parsing happened before cancellation
            }
            catch (Exception ex)
            {
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
                    try { File.Delete(scriptPath); }
                    catch { /* Ignore cleanup error */ }
                }
                // Keep the log file for inspection
                result.LogMessages.Add($"Log file kept at: {logPath}");
            }

            return result;
        }

        // --- Remove ParseSteamCmdOutput method as it was for live parsing ---
        // private void ParseSteamCmdOutput(...) { ... }
    }
}
