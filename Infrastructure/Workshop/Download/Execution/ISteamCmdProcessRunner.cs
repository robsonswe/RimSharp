#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Infrastructure.Workshop.Download.Execution
{
    public interface ISteamCmdProcessRunner
    {
        Task<SteamCmdRunResult> RunSteamCmdAsync(
            string scriptPath,
            string primaryLogPath,
            string workingDirectory,
            CancellationToken cancellationToken);
    }
}