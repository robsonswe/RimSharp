#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop.Download.Parsing
{
    public class SteamCmdLogParser : ISteamCmdLogParser
    {
        private readonly ILoggerService _logger;
        private const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss";
        private const int LogSampleSize = 30; // Sample size per log file

        // Workshop Log (Refined slightly for clarity)
        private static readonly Regex WorkshopLogSuccessRegex = new Regex(
             @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*OK",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WorkshopLogFailureRegex = new Regex(
            @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Download item\s+(\d+)\s+result\s*:\s*(?!OK\s*$)(\w+[\w\s]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
         private static readonly Regex WorkshopConnectionFailureRegex = new Regex(
            @"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s+\[AppID\s+294100\]\s+Workshop download job .* failed with error", // More general workshop error
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

// Console Log (Primary Execution Log)
        private static readonly Regex ConsoleLoginSuccessRegex = new Regex(
            @"^\s*Connecting\s+anonymously\s+to\s+Steam\s+Public\.\.\.\s*OK", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ConsoleLoginFailureRegex = new Regex(
            @"^\s*FAILED\s+to\s+log\s+in", RegexOptions.Compiled | RegexOptions.IgnoreCase); // Look for variations
         private static readonly Regex ConsoleConnectionFailureRegex = new Regex(
            @"Steam Console Client.*?\s+Connect\(.*?\)\s+failed", RegexOptions.Compiled | RegexOptions.IgnoreCase); // General connection failure
        private static readonly Regex ConsoleTimeoutErrorRegex = new Regex(
            @"^ERROR!\s+Timeout\s+downloading\s+item\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ConsoleDownloadItemFailedRegex = new Regex(
             @"^ERROR!\s+Download\s+item\s+(\d+)\s+failed\s+\((.+)\)\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
         private static readonly Regex ConsoleCommandNotFoundRegex = new Regex(
            @"^Command\s+not\s+found:\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase); // Script error

        // Content Log
         private static readonly Regex ContentValidationFailedRegex = new Regex(
            @"Validation: FAILED", RegexOptions.Compiled | RegexOptions.IgnoreCase);
         private static readonly Regex ContentValidationMissingFileRegex = new Regex(
            @"Validation:\s+missing\s+file\s+""?(\d+)[\\/](.+?)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase); // Capture item ID if possible
        private static readonly Regex ContentUpdateCancelledRegex = new Regex(
            @"AppID\s+\d+\s+update\s+canceled:\s*(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ContentDiskWriteFailureRegex = new Regex(
            @"(Disk write failure|Failed to write chunk .*? to disk)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ContentDiskSpaceRegex = new Regex(
            @"Not enough disk space", RegexOptions.Compiled | RegexOptions.IgnoreCase);

// Bootstrap Log
        private static readonly Regex BootstrapUpdateErrorRegex = new Regex(
            @"^Error:\s+Download\s+of\s+package\s+\((.+)\)\s+failed", RegexOptions.Compiled | RegexOptions.IgnoreCase);
         private static readonly Regex BootstrapFailedLoadHostRegex = new Regex(
            @"^Failed\s+to\s+load\s+cached\s+hosts\s+file", RegexOptions.Compiled | RegexOptions.IgnoreCase);

public SteamCmdLogParser(ILoggerService logger)
        {
            _logger = logger;
        }

        public async Task<SteamCmdSessionLogParseResult> ParseSteamCmdSessionLogsAsync(
            SteamCmdLogFilePaths logPaths,
            ISet<string> idsToLookFor,
            DateTime filterTime,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug($"Starting comprehensive log parsing session. Filter time >= {filterTime:O}", "SteamCmdLogParser");
            var result = new SteamCmdSessionLogParseResult();
            var logData = new Dictionary<string, string[]>();
            await ReadLogFileAsync("Workshop", logPaths.WorkshopLogPath, result, logData, cancellationToken);
            await ReadLogFileAsync("Primary", logPaths.PrimaryExecutionLogPath, result, logData, cancellationToken);
            await ReadLogFileAsync("Content", logPaths.ContentLogPath, result, logData, cancellationToken);
            await ReadLogFileAsync("Bootstrap", logPaths.BootstrapLogPath, result, logData, cancellationToken);
            ParseWorkshopLog(logData.GetValueOrDefault("Workshop"), idsToLookFor, filterTime, result, cancellationToken);
            ParsePrimaryLog(logData.GetValueOrDefault("Primary"), idsToLookFor, filterTime, result, cancellationToken);
            ParseContentLog(logData.GetValueOrDefault("Content"), idsToLookFor, filterTime, result, cancellationToken);
            ParseBootstrapLog(logData.GetValueOrDefault("Bootstrap"), filterTime, result, cancellationToken); // Bootstrap log isn't usually item-specific
            if (result.OverallStatus.HasLoginFailed)
            {
                _logger.LogWarning("Login failure detected in logs. Marking all requested items as failed for this session.", "SteamCmdLogParser");
                result.AddCriticalMessage("SteamCMD login failed during this session.");
                foreach (var id in idsToLookFor)
                {

                    if (!result.WorkshopItemResults.TryGetValue(id, out var itemResult) || itemResult.Success)
                    {

                        result.WorkshopItemResults[id] = (Success: false, Timestamp: filterTime, Reason: "Login Failure");
                    }
                }
            }

_logger.LogInfo($"Log parsing finished. Found {result.WorkshopItemResults.Count} item results. Overall Status Flags: {result.OverallStatus.Flags}", "SteamCmdLogParser");
            if (result.CriticalMessages.Any())
            {
                 _logger.LogWarning($"Critical messages detected during parsing: {result.CriticalMessages.Count}", "SteamCmdLogParser");
                 foreach (var msg in result.CriticalMessages) { _logger.LogDebug($"- {msg}", "SteamCmdLogParser"); }
            }

            return result;
        }

        private async Task ReadLogFileAsync(string logType, string? logPath, SteamCmdSessionLogParseResult result, Dictionary<string, string[]> logData, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
            {
                _logger.LogDebug($"Log file '{logType}' not found or path not provided: {logPath ?? "N/A"}", "SteamCmdLogParser");
                return;
            }

            try
            {
                 _logger.LogDebug($"Reading log file '{logType}': {Path.GetFileName(logPath)}", "SteamCmdLogParser");

                 using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                 using var sr = new StreamReader(fs, Encoding.UTF8); // Specify UTF8, common for logs

                 var lines = new List<string>();
                 string? line;
                 while ((line = await sr.ReadLineAsync(cancellationToken)) != null)
                 {
                     lines.Add(line);
                 }
                 logData[logType] = lines.ToArray();
                 result.LogSamples[logType] = lines.TakeLast(LogSampleSize).ToList();
                 _logger.LogDebug($"Read {lines.Count} lines from '{logType}' log.", "SteamCmdLogParser");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                 _logger.LogError($"Error reading log file '{logType}' ({logPath}): {ex.Message}", "SteamCmdLogParser");
                 result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.GeneralError);
                 result.AddCriticalMessage($"Error reading {logType} log: {ex.Message}");
            }
        }

        private void ParseWorkshopLog(string[]? lines, ISet<string> idsToLookFor, DateTime filterTime, SteamCmdSessionLogParseResult result, CancellationToken cancellationToken)
        {
            if (lines == null) return;
            _logger.LogDebug($"Parsing Workshop Log ({lines.Length} lines)...", "SteamCmdLogParser");
            int processedCount = 0;

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryParseTimestamp(line, out var timestamp, out var lineContent))
                {
                    if (timestamp < filterTime) continue; // Skip entries before this session attempt

                    // Success Match
                    Match successMatch = WorkshopLogSuccessRegex.Match(line);
                    if (successMatch.Success)
                    {
                        string id = successMatch.Groups[2].Value;
                        if (idsToLookFor.Contains(id))
                        {
                             processedCount++;
                             UpdateItemResult(id, true, timestamp, null, result);
                        }
                        continue; // Move to next line
                    }

                    // Failure Match
                    Match failureMatch = WorkshopLogFailureRegex.Match(line);
                    if (failureMatch.Success)
                    {
                        string id = failureMatch.Groups[2].Value;
                        string reason = failureMatch.Groups[3].Value.Trim();
                         if (idsToLookFor.Contains(id))
                         {
                             processedCount++;
                             UpdateItemResult(id, false, timestamp, reason, result);
                             if (reason.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                             {
                                 result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.TimeoutDetected);
                             }
                             result.AddCriticalMessage($"Workshop log failure for {id}: {reason}");
                         }
                         continue;
                    }

                     // General Workshop Connection/Download Failure
                     Match generalFailureMatch = WorkshopConnectionFailureRegex.Match(line);
                     if (generalFailureMatch.Success)
                     {
                         result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.WorkshopConnectionError);
                         result.AddCriticalMessage($"Workshop log general failure: {lineContent}");
                     }
                }
            }
            result.ProcessedWorkshopEntryCount = processedCount;
            _logger.LogDebug($"Workshop log parsing finished. Processed {processedCount} relevant entries.", "SteamCmdLogParser");
        }

        private void ParsePrimaryLog(string[]? lines, ISet<string> idsToLookFor, DateTime filterTime, SteamCmdSessionLogParseResult result, CancellationToken cancellationToken)
        {
             if (lines == null) return;
             _logger.LogDebug($"Parsing Primary Execution Log ({lines.Length} lines)...", "SteamCmdLogParser");

             foreach (var line in lines)
             {
                 cancellationToken.ThrowIfCancellationRequested();

// Login Status
                 if (ConsoleLoginSuccessRegex.IsMatch(line))
                 {
                     result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.LoginAttempted);
                     result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.LoginSuccess);
                     result.OverallStatus.Flags &= ~SteamCmdSessionStatusFlags.LoginFailure;
                     continue;
                 }
                 if (ConsoleLoginFailureRegex.IsMatch(line))
                 {
                     result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.LoginAttempted);
                     result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.LoginFailure);
                     result.AddCriticalMessage($"Primary log: Login failure detected: {line.Trim()}");
                     continue;
                 }
                  if (ConsoleConnectionFailureRegex.IsMatch(line))
                 {
                     result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.ConnectionError);
                     result.AddCriticalMessage($"Primary log: Connection failure: {line.Trim()}");
                     continue;
                 }

// Item Timeout
                 Match timeoutMatch = ConsoleTimeoutErrorRegex.Match(line);
                 if (timeoutMatch.Success)
                 {
                     string id = timeoutMatch.Groups[1].Value;
                      if (idsToLookFor.Contains(id))
                      {

                          UpdateItemResult(id, false, filterTime, "Timeout", result);
                          result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.TimeoutDetected);
                          result.AddCriticalMessage($"Primary log timeout for {id}.");
                      }
                      continue;
                 }

                  // Item Generic Failure
                 Match itemFailedMatch = ConsoleDownloadItemFailedRegex.Match(line);
                 if (itemFailedMatch.Success)
                 {
                     string id = itemFailedMatch.Groups[1].Value;
                     string reason = itemFailedMatch.Groups[2].Value.Trim();
                     if (idsToLookFor.Contains(id))
                     {
                         UpdateItemResult(id, false, filterTime, reason, result);
                         result.AddCriticalMessage($"Primary log failure for {id}: {reason}");
                         if (reason.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                         {
                             result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.TimeoutDetected);
                         }
                         else if (reason.Contains("Disk", StringComparison.OrdinalIgnoreCase))
                         {
                             result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.DiskWriteError); // Or DiskSpaceIssue
                         }
                     }
                     continue;
                 }

                 // Script Error
                 Match cmdNotFoundMatch = ConsoleCommandNotFoundRegex.Match(line);
                 if (cmdNotFoundMatch.Success)
                 {
                     string command = cmdNotFoundMatch.Groups[1].Value.Trim();
                     if (command.StartsWith("force_install_dir", StringComparison.OrdinalIgnoreCase) ||
                         command.StartsWith("login", StringComparison.OrdinalIgnoreCase) ||
                         command.StartsWith("workshop_download_item", StringComparison.OrdinalIgnoreCase) ||
                         command.StartsWith("@ShutdownOnFailedCommand", StringComparison.OrdinalIgnoreCase) ||
                         command.StartsWith("quit", StringComparison.OrdinalIgnoreCase))
                     {
                          result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.ScriptError);
                          result.AddCriticalMessage($"Primary log: Script error - Command not found: '{command}'");
                     }

continue;
                 }
             }
             _logger.LogDebug("Primary log parsing finished.", "SteamCmdLogParser");
        }

         private void ParseContentLog(string[]? lines, ISet<string> idsToLookFor, DateTime filterTime, SteamCmdSessionLogParseResult result, CancellationToken cancellationToken)
         {
             if (lines == null) return;
             _logger.LogDebug($"Parsing Content Log ({lines.Length} lines)...", "SteamCmdLogParser");
             string? lastItemIdMentioned = null; // Try to track context

             foreach (var line in lines)
             {
                 cancellationToken.ThrowIfCancellationRequested();
                 if (TryParseTimestamp(line, out var timestamp, out var lineContent))
                 {
                     if (timestamp < filterTime) continue;

                     // Track last item ID mentioned in validation lines
                     Match missingFileMatch = ContentValidationMissingFileRegex.Match(line);
                     if (missingFileMatch.Success)
                     {
                         lastItemIdMentioned = missingFileMatch.Groups[1].Value;
                         result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.ValidationError);

continue; // Prioritize this match
                     }

                     if (ContentValidationFailedRegex.IsMatch(line))
                     {
                         result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.ValidationError);
                         result.AddCriticalMessage($"Content log: Validation FAILED (Item context: {lastItemIdMentioned ?? "Unknown"})");
                         continue;
                     }

                     if (ContentDiskWriteFailureRegex.IsMatch(line))
                     {
                         result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.DiskWriteError);
                         result.AddCriticalMessage($"Content log: Disk write failure detected.");
                         continue;
                     }
                     if (ContentDiskSpaceRegex.IsMatch(line))
                     {
                         result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.DiskSpaceIssue);
                         result.AddCriticalMessage($"Content log: Disk space issue detected.");
                         continue;
                     }

                     Match updateCancelledMatch = ContentUpdateCancelledRegex.Match(line);
                     if (updateCancelledMatch.Success)
                     {
                         string reason = updateCancelledMatch.Groups[1].Value.Trim();
                         result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.GeneralError); // Or more specific if possible
                         result.AddCriticalMessage($"Content log: Update canceled - Reason: {reason}");
                          if (reason.Contains("Missing game files", StringComparison.OrdinalIgnoreCase))
                         {
                              result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.ValidationError);
                         }
                         // Attempt to associate with last known item? Risky.
                         continue;
                     }
                 }
             }
             _logger.LogDebug("Content log parsing finished.", "SteamCmdLogParser");
         }

        private void ParseBootstrapLog(string[]? lines, DateTime filterTime, SteamCmdSessionLogParseResult result, CancellationToken cancellationToken)
        {
            if (lines == null) return;
             _logger.LogDebug($"Parsing Bootstrap Log ({lines.Length} lines)...", "SteamCmdLogParser");

             foreach (var line in lines)
             {
                 cancellationToken.ThrowIfCancellationRequested();

Match updateErrorMatch = BootstrapUpdateErrorRegex.Match(line);
                 if (updateErrorMatch.Success)
                 {
                      result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.SteamCmdUpdateError);
                      result.AddCriticalMessage($"Bootstrap log: SteamCMD update error: {line.Trim()}");
                      continue;
                 }

                 if (BootstrapFailedLoadHostRegex.IsMatch(line))
                 {
                     result.OverallStatus.AddFlag(SteamCmdSessionStatusFlags.ConnectionError); // Could indicate network config issue
                     result.AddCriticalMessage($"Bootstrap log: Failed to load cached hosts: {line.Trim()}");
                     continue;
                 }
             }
             _logger.LogDebug("Bootstrap log parsing finished.", "SteamCmdLogParser");
        }

        /// <summary>

/// </summary>
        private void UpdateItemResult(string id, bool success, DateTime timestamp, string? reason, SteamCmdSessionLogParseResult result)
        {
             if (result.WorkshopItemResults.TryGetValue(id, out var existing))
             {

                 if (success && existing.Success && timestamp > existing.Timestamp)
                 {
                     result.WorkshopItemResults[id] = (success, timestamp, reason);
                     _logger.LogDebug($"Updated item {id} result: Success at {timestamp:O} (was Success at {existing.Timestamp:O})", "SteamCmdLogParser");
                 }

                 else if (!success && (timestamp > existing.Timestamp || existing.Success))
                 {
                     result.WorkshopItemResults[id] = (success, timestamp, reason);
                      _logger.LogDebug($"Updated item {id} result: Failure ('{reason}') at {timestamp:O} (was Success: {!existing.Success} at {existing.Timestamp:O})", "SteamCmdLogParser");
                 }

             }
             else
             {
                 // No existing entry, just add it
                 result.WorkshopItemResults.Add(id, (success, timestamp, reason));
                 _logger.LogDebug($"Added item {id} result: Success: {success} at {timestamp:O} (Reason: {reason ?? "N/A"})", "SteamCmdLogParser");
             }
        }

/// <summary>

        /// </summary>
        private bool TryParseTimestamp(string line, out DateTime timestamp, out string remainingContent)
        {
            timestamp = DateTime.MinValue;
            remainingContent = line;

            if (line != null && line.Length > 21 && line[0] == '[' && line[20] == ']')
            {
                string timestampStr = line.Substring(1, 19);
                if (DateTime.TryParseExact(timestampStr, LogTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
                {
                    remainingContent = line.Substring(21).TrimStart();
                    return true;
                }
            }
            return false;
        }
    }
}


