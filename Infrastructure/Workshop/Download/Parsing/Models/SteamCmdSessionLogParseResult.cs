#nullable enable
using System;
using System.Collections.Generic;

namespace RimSharp.Infrastructure.Workshop.Download.Parsing.Models
{
    /// <summary>
    /// Represents the consolidated results from parsing multiple SteamCMD log files
    /// for a specific execution session.
    /// </summary>
    public class SteamCmdSessionLogParseResult
    {
        /// <summary>
        /// Contains the latest status found *across all parsed logs* for each relevant ID
        /// within the specified timeframe.
        /// Key: Steam Workshop Item ID (string)
        /// Value: Tuple (bool Success, DateTime Timestamp, string? FailureReason)
        /// </summary>
        public Dictionary<string, (bool Success, DateTime Timestamp, string? Reason)> WorkshopItemResults { get; }

        /// <summary>
        /// Indicates the overall health and status detected during the SteamCMD session.
        /// </summary>
        public SteamCmdSessionStatus OverallStatus { get; }

        /// <summary>
        /// A collection of significant error or warning messages extracted from any of the parsed logs.
        /// Useful for diagnosing failures when item-specific results are unclear.
        /// </summary>
        public List<string> CriticalMessages { get; }

        /// <summary>
        /// Samples of the last N lines read from each parsed log file for debugging.
        /// Key: Log file type identifier (e.g., "Workshop", "Console", "Content")
        /// Value: List of log lines.
        /// </summary>
        public Dictionary<string, List<string>> LogSamples { get; }

        /// <summary>
        /// The number of workshop log entries that were successfully parsed and matched the criteria (ID + timestamp).
        /// </summary>
        public int ProcessedWorkshopEntryCount { get; internal set; } // Allow internal modification

        public SteamCmdSessionLogParseResult()
        {
            WorkshopItemResults = new Dictionary<string, (bool Success, DateTime Timestamp, string? Reason)>();
            OverallStatus = new SteamCmdSessionStatus();
            CriticalMessages = new List<string>();
            LogSamples = new Dictionary<string, List<string>>();
            ProcessedWorkshopEntryCount = 0;
        }

        // Convenience method to add a critical message, avoiding duplicates maybe?
        public void AddCriticalMessage(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !CriticalMessages.Contains(message.Trim()))
            {
                CriticalMessages.Add(message.Trim());
            }
        }
    }

    /// <summary>
    /// Flags indicating the general status detected during a SteamCMD session based on log analysis.
    /// </summary>
    [Flags]
    public enum SteamCmdSessionStatusFlags
    {
        None = 0,
        LoginAttempted = 1 << 0,
        LoginSuccess = 1 << 1,
        LoginFailure = 1 << 2,
        ConnectionError = 1 << 3, // General connection problems
        WorkshopConnectionError = 1 << 4, // Specific workshop connection issues
        DiskWriteError = 1 << 5,
        DiskSpaceIssue = 1 << 6,
        ValidationError = 1 << 7, // Content validation failed
        ScriptError = 1 << 8, // e.g., Command not found
        TimeoutDetected = 1 << 9, // Timeout reported in console/workshop log
        SteamCmdUpdateError = 1 << 10, // Error during bootstrap/self-update
        GeneralError = 1 << 11 // Catch-all for other logged errors
    }

    /// <summary>
    /// Holds the overall status flags detected during the SteamCMD session.
    /// </summary>
    public class SteamCmdSessionStatus
    {
        public SteamCmdSessionStatusFlags Flags { get; internal set; } = SteamCmdSessionStatusFlags.None;

        public bool HasFlag(SteamCmdSessionStatusFlags flag) => Flags.HasFlag(flag);
        public void AddFlag(SteamCmdSessionStatusFlags flag) => Flags |= flag;

        // Convenience properties
        public bool IsLoginSuccessful => HasFlag(SteamCmdSessionStatusFlags.LoginSuccess);
        public bool HasLoginFailed => HasFlag(SteamCmdSessionStatusFlags.LoginFailure);
        public bool HasConnectionError => HasFlag(SteamCmdSessionStatusFlags.ConnectionError) || HasFlag(SteamCmdSessionStatusFlags.WorkshopConnectionError);
        public bool HasDiskError => HasFlag(SteamCmdSessionStatusFlags.DiskWriteError) || HasFlag(SteamCmdSessionStatusFlags.DiskSpaceIssue);
        public bool HasScriptError => HasFlag(SteamCmdSessionStatusFlags.ScriptError);
        public bool HasTimeout => HasFlag(SteamCmdSessionStatusFlags.TimeoutDetected);
        public bool HasAnyError => HasFlag(SteamCmdSessionStatusFlags.LoginFailure)
                                   || HasFlag(SteamCmdSessionStatusFlags.ConnectionError)
                                   || HasFlag(SteamCmdSessionStatusFlags.WorkshopConnectionError)
                                   || HasFlag(SteamCmdSessionStatusFlags.DiskWriteError)
                                   || HasFlag(SteamCmdSessionStatusFlags.DiskSpaceIssue)
                                   || HasFlag(SteamCmdSessionStatusFlags.ValidationError)
                                   || HasFlag(SteamCmdSessionStatusFlags.ScriptError)
                                   || HasFlag(SteamCmdSessionStatusFlags.TimeoutDetected)
                                   || HasFlag(SteamCmdSessionStatusFlags.SteamCmdUpdateError)
                                   || HasFlag(SteamCmdSessionStatusFlags.GeneralError);
    }
}
