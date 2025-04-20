#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models; // Re-use existing Models namespace

namespace RimSharp.Infrastructure.Workshop.Download.Parsing
{
    /// <summary>
    /// Parses multiple log files from a SteamCMD session to determine overall status
    /// and specific item download results.
    /// </summary>
    public interface ISteamCmdLogParser
    {
        /// <summary>
        /// Parses the relevant log files generated during a SteamCMD execution attempt.
        /// </summary>
        /// <param name="logPaths">A collection of paths to the relevant log files for the session.</param>
        /// <param name="idsToLookFor">The specific Workshop Item IDs whose status needs to be determined.</param>
        /// <param name="filterTime">Only consider log entries at or after this time (relevant to the specific execution attempt).</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A comprehensive result object containing parsed information from all logs.</returns>
        Task<SteamCmdSessionLogParseResult> ParseSteamCmdSessionLogsAsync(
            SteamCmdLogFilePaths logPaths,
            ISet<string> idsToLookFor,
            DateTime filterTime,
            CancellationToken cancellationToken);
    }
}
