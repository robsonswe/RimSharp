using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Services.ModManagement
{
    /// <summary>
    /// Service for running fast, read-only troubleshooting scans.
    /// </summary>
    public interface ITroubleshootingScanService
    {
        /// <summary>
        /// Runs all applicable scan checks and returns findings.
        /// </summary>
        Task<IReadOnlyList<ScanFinding>> RunScanAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs only the checks whose IDs are in <paramref name="checkIds"/>, applying the same
        /// read-only semantics and OS/Steam/version gates as <see cref="RunScanAsync"/>.
        /// Gate-unmet checks are skipped silently and are not failures.
        /// </summary>
        Task<IReadOnlyList<ScanFinding>> RunChecksAsync(IReadOnlyCollection<string> checkIds, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a single scan finding.
    /// </summary>
    public sealed class ScanFinding
    {
        public string CheckId { get; init; } = string.Empty;
        public bool Passed { get; init; }
        public ScanSeverity Severity { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string? SuggestedActionId { get; init; }
        public IReadOnlyDictionary<string, object>? Metadata { get; init; }
    }

    /// <summary>
    /// Severity levels for scan findings.
    /// </summary>
    public enum ScanSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Result of a full scan run, including gate status.
    /// </summary>
    public sealed class ScanResult
    {
        public IReadOnlyList<ScanFinding> Findings { get; init; } = Array.Empty<ScanFinding>();
        public bool IsSteamInstallation { get; init; }
        public bool IsWindows { get; init; }
        public string GameVersion { get; init; } = string.Empty;
        public bool UnityCrashHandlerRelevant { get; init; }
    }
}