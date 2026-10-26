#nullable enable
namespace RimSharp.Infrastructure.Workshop.Download.Execution
{
    /// <summary>
    /// Result of a SteamCMD process run. <see cref="CapturedOutput"/> holds the
    /// full stdout/stderr of the process (captured on all platforms), so fatal
    /// errors (e.g. missing disk space) can be surfaced to the user instead of
    /// a generic "key log files missing" message.
    /// </summary>
    public record SteamCmdRunResult(int ExitCode, string CapturedOutput);
}
