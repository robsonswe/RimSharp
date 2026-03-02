#nullable enable
namespace RimSharp.Infrastructure.Workshop.Download.Parsing.Models
{
    /// <summary>

/// </summary>
    public class SteamCmdLogFilePaths
    {
        /// <summary>
        /// Path to logs/workshop_log.txt (detailed item download status).
        /// </summary>
        public string? WorkshopLogPath { get; set; }

        /// <summary>

        /// Contains login status, command execution, and general errors.
        /// </summary>
        public string? PrimaryExecutionLogPath { get; set; } // Renamed from ConsoleLogPath for clarity

        /// <summary>
        /// Path to logs/content_log.txt (details on content download/validation).
        /// </summary>
        public string? ContentLogPath { get; set; }

        /// <summary>
        /// Path to logs/bootstrap_log.txt (details on SteamCMD self-update/startup).
        /// Optional, but can indicate startup issues.
        /// </summary>
        public string? BootstrapLogPath { get; set; }

    }
}


