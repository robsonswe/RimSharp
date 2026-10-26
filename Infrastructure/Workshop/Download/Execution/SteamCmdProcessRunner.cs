#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop.Download.Execution
{
    public class SteamCmdProcessRunner : ISteamCmdProcessRunner
    {
        private readonly ISteamCmdPathService _pathService;
        private readonly ILoggerService _logger;

        public SteamCmdProcessRunner(ISteamCmdPathService pathService, ILoggerService logger)
        {
            _pathService = pathService;
            _logger = logger;
        }

        public async Task<SteamCmdRunResult> RunSteamCmdAsync(
            string scriptPath,
            string primaryLogPath,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_pathService.SteamCmdExePath) || !File.Exists(_pathService.SteamCmdExePath))
            {
                _logger.LogError($"SteamCMD executable not found or configured: {_pathService.SteamCmdExePath}", "SteamCmdProcessRunner");
                throw new FileNotFoundException("SteamCMD executable path is invalid or not configured.", _pathService.SteamCmdExePath);
            }

            if (!File.Exists(scriptPath))
            {
                 _logger.LogError($"SteamCMD script file not found: {scriptPath}", "SteamCmdProcessRunner");
                throw new FileNotFoundException("SteamCMD script file not found.", scriptPath);
            }

            if (!Directory.Exists(workingDirectory))
            {
                 _logger.LogError($"SteamCMD working directory not found: {workingDirectory}", "SteamCmdProcessRunner");
                throw new DirectoryNotFoundException($"SteamCMD working directory not found: {workingDirectory}");
            }

            try
            {
                if (File.Exists(primaryLogPath))
                {
                    File.Delete(primaryLogPath);
                    _logger.LogDebug($"Deleted previous primary log file: {primaryLogPath}", "SteamCmdProcessRunner");
                }
            }
            catch (IOException ex)
            {
                 _logger.LogWarning($"Could not delete previous primary log file '{primaryLogPath}': {ex.Message}", "SteamCmdProcessRunner");
                // Continue anyway
            }

// Capture SteamCMD output on all platforms so fatal errors (e.g. missing
// disk space) can be surfaced to the user instead of a generic
// "key log files missing" message. This replaces the old visible console
// window: output now goes to the app log and, on failure, to the UI.
var processStartInfo = new ProcessStartInfo
            {
                FileName = _pathService.SteamCmdExePath,
                // NOTE: no "+log_file" argument — it is not a SteamCMD command
                // ("Command not found: log_file"); SteamCMD's real logs go to
                // its Steam dir "logs" folder (see SteamCmdLogDirectory).
                Arguments = $"+runscript \"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            };

            if (!OperatingSystem.IsWindows())
            {
                // On Linux/macOS SteamCMD resolves its logging directory from
                // the HOME environment variable (writes to "$HOME/Steam/logs").
                // Override it so logs land in an app-managed directory next to
                // the SteamCMD data instead of the user's home directory.
                string steamHomeDir = Path.Combine(_pathService.SteamCmdPrefixPath, "steam-home");
                Directory.CreateDirectory(steamHomeDir);
                processStartInfo.EnvironmentVariables["HOME"] = steamHomeDir;
                _logger.LogDebug($"SteamCMD HOME override (log redirection): {steamHomeDir}", "SteamCmdProcessRunner");
            }

            _logger.LogInfo($"Executing SteamCMD: \"{processStartInfo.FileName}\" {processStartInfo.Arguments}", "SteamCmdProcessRunner");
            _logger.LogDebug($"Working Directory: {processStartInfo.WorkingDirectory}", "SteamCmdProcessRunner");
            _logger.LogDebug($"Primary Log Target: {primaryLogPath}", "SteamCmdProcessRunner");

using var process = new Process { StartInfo = processStartInfo };
var outputBuilder = new StringBuilder();

            try
            {
                if (!process.Start())
                {
                     _logger.LogError("Failed to start SteamCMD process.", "SteamCmdProcessRunner");
                     return new SteamCmdRunResult(-999, string.Empty); // Indicate process start failure
                }
                _logger.LogDebug($"SteamCMD process started (PID: {process.Id})", "SteamCmdProcessRunner");

                // Capture stdout/stderr: log each line and buffer it so the
                // downloader can extract fatal errors when SteamCMD dies
                // before writing its own log files.
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    lock (outputBuilder) { outputBuilder.AppendLine(e.Data); }
                    _logger.LogDebug($"[steamcmd] {e.Data}", "SteamCmdProcessRunner");
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    lock (outputBuilder) { outputBuilder.AppendLine(e.Data); }
                    _logger.LogDebug($"[steamcmd-err] {e.Data}", "SteamCmdProcessRunner");
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                _logger.LogInfo($"SteamCMD process exited with code: {process.ExitCode}", "SteamCmdProcessRunner");
                string captured;
                lock (outputBuilder) { captured = outputBuilder.ToString(); }
                return new SteamCmdRunResult(process.ExitCode, captured);
            }
            catch (System.ComponentModel.Win32Exception ex) // More specific exception for start failure
            {
                 _logger.LogError($"Failed to start SteamCMD process: {ex.Message} (NativeErrorCode: {ex.NativeErrorCode})", "SteamCmdProcessRunner");
                 return new SteamCmdRunResult(-998, string.Empty); // Indicate Win32 exception on start
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SteamCMD process execution cancelled.", "SteamCmdProcessRunner");
                try { if (!process.HasExited) process.Kill(true); } catch { /* Ignore kill errors */ }
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error running SteamCMD process: {ex.Message}", "SteamCmdProcessRunner");
                 try { if (!process.HasExited) process.Kill(true); } catch { /* Ignore kill errors */ }
                 string captured;
                 lock (outputBuilder) { captured = outputBuilder.ToString(); }
                 return new SteamCmdRunResult(-997, captured); // Indicate unexpected error during execution
            }
        }
    }
}

