#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Workshop.Download.Parsing.Models; // Re-use existing Models namespace

namespace RimSharp.Infrastructure.Workshop.Download.Parsing
{
    /// <summary>

    /// and specific item download results.
    /// </summary>
    public interface ISteamCmdLogParser
    {
        /// <summary>

        /// </summary>

/// <param name="cancellationToken">Token to monitor for cancellation requests.</param>

        Task<SteamCmdSessionLogParseResult> ParseSteamCmdSessionLogsAsync(
            SteamCmdLogFilePaths logPaths,
            ISet<string> idsToLookFor,
            DateTime filterTime,
            CancellationToken cancellationToken);
    }
}

