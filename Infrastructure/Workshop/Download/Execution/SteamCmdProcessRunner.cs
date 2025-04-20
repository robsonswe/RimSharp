#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Workshop.Core;
using RimSharp.Shared.Services.Contracts; // For ILoggerService, ISteamCmdPathService

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

        public async Task<int> RunSteamCmdAsync(
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


            // Attempt to delete previous primary log if it exists
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


            var processStartInfo = new ProcessStartInfo
            {
                FileName = _pathService.SteamCmdExePath,
                Arguments = $"+runscript \"{scriptPath}\" +log_file \"{primaryLogPath}\"",
                UseShellExecute = true, // Keep true for visibility or set false for more control (if needed)
                RedirectStandardOutput = false, // Depends on UseShellExecute
                RedirectStandardError = false,  // Depends on UseShellExecute
                CreateNoWindow = false, // Show window (as was original)
                WorkingDirectory = workingDirectory,
            };

            _logger.LogInfo($"Executing SteamCMD: \"{processStartInfo.FileName}\" {processStartInfo.Arguments}", "SteamCmdProcessRunner");
            _logger.LogDebug($"Working Directory: {processStartInfo.WorkingDirectory}", "SteamCmdProcessRunner");
            _logger.LogDebug($"Primary Log Target: {primaryLogPath}", "SteamCmdProcessRunner");


            using var process = new Process { StartInfo = processStartInfo };

            try
            {
                if (!process.Start())
                {
                     _logger.LogError("Failed to start SteamCMD process.", "SteamCmdProcessRunner");
                     return -999; // Indicate process start failure
                }
                _logger.LogDebug($"SteamCMD process started (PID: {process.Id})", "SteamCmdProcessRunner");

                await process.WaitForExitAsync(cancellationToken);

                _logger.LogInfo($"SteamCMD process exited with code: {process.ExitCode}", "SteamCmdProcessRunner");
                return process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception ex) // More specific exception for start failure
            {
                 _logger.LogError($"Failed to start SteamCMD process: {ex.Message} (NativeErrorCode: {ex.NativeErrorCode})", "SteamCmdProcessRunner");
                 return -998; // Indicate Win32 exception on start
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
                return -997; // Indicate unexpected error during execution
            }
        }
    }
}