using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop
{
    /// <summary>
    /// Handles downloading Workshop content using SteamCMD.
    /// </summary>
    public class SteamCmdDownloader : ISteamCmdDownloader
    {
        private const string RimworldAppId = "294100";
        
        private readonly ISteamCmdPathService _pathService;
        private readonly ISteamCmdInstaller _installer;
        private readonly IDialogService _dialogService;

        public SteamCmdDownloader(
            ISteamCmdPathService pathService,
            ISteamCmdInstaller installer,
            IDialogService dialogService)
        {
            _pathService = pathService;
            _installer = installer;
            _dialogService = dialogService;
        }

        public async Task<bool> DownloadModsAsync(
            IEnumerable<string> workshopIds, 
            bool validate, 
            IProgress<string>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            bool isSetupComplete = await _installer.CheckSetupAsync();
            if (!isSetupComplete || string.IsNullOrEmpty(_pathService.SteamCmdExePath))
            {
                progress?.Report("Download failed: SteamCMD is not set up.");
                return false;
            }

            if (!workshopIds.Any())
            {
                progress?.Report("Download skipped: No workshop IDs provided.");
                return true; // Nothing to do is success
            }

            string scriptPath = Path.Combine(Path.GetTempPath(), $"steamcmd_script_{Guid.NewGuid()}.txt");
            string logPath = Path.Combine(Path.GetTempPath(), $"steamcmd_log_{Guid.NewGuid()}.log"); // Log file for detailed output

            try
            {
                // --- 1. Create Script ---
                progress?.Report("Generating SteamCMD script...");
                var scriptBuilder = new StringBuilder();
                scriptBuilder.AppendLine($"force_install_dir \"{_pathService.SteamCmdSteamAppsPath}\"");
                scriptBuilder.AppendLine($"logfile \"{logPath}\""); // Log output to a file
                scriptBuilder.AppendLine("login anonymous");

                string downloadCommand = $"workshop_download_item {RimworldAppId}";
                string validateSuffix = validate ? " validate" : "";
                int totalMods = workshopIds.Count();
                int currentMod = 0;
                
                foreach (string id in workshopIds)
                {
                    currentMod++;
                    if (!string.IsNullOrWhiteSpace(id) && long.TryParse(id.Trim(), out _))
                    {
                        scriptBuilder.AppendLine($"{downloadCommand} {id.Trim()}{validateSuffix}");
                        progress?.Report($"Script: Added download for {id} ({currentMod}/{totalMods})");
                    } 
                    else 
                    {
                        progress?.Report($"Script: Skipped invalid ID '{id}' ({currentMod}/{totalMods})");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                scriptBuilder.AppendLine("quit");

                await File.WriteAllTextAsync(scriptPath, scriptBuilder.ToString(), cancellationToken);
                progress?.Report($"SteamCMD script written to: {scriptPath}");
                progress?.Report($"SteamCMD log will be written to: {logPath}");

                cancellationToken.ThrowIfCancellationRequested();

                // --- 2. Execute SteamCMD ---
                progress?.Report($"Executing SteamCMD: {_pathService.SteamCmdExePath} +runscript \"{scriptPath}\"");

                // Delete old log file if it exists
                if (File.Exists(logPath)) File.Delete(logPath);

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = _pathService.SteamCmdExePath,
                    Arguments = $"+runscript \"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = processStartInfo };

                // Handle output data received events to report progress
                process.OutputDataReceived += (sender, args) => 
                { 
                    if (args.Data != null) 
                        progress?.Report($"SteamCMD: {args.Data}"); 
                };
                
                process.ErrorDataReceived += (sender, args) => 
                { 
                    if (args.Data != null) 
                        progress?.Report($"SteamCMD ERR: {args.Data}"); 
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for exit or cancellation
                await process.WaitForExitAsync(cancellationToken);

                progress?.Report($"SteamCMD process exited with code: {process.ExitCode}");

                // Read the full log file after completion for detailed info
                if (File.Exists(logPath))
                {
                    try 
                    {
                        string logContent = await File.ReadAllTextAsync(logPath, cancellationToken);
                        progress?.Report("--- SteamCMD Log File Start ---");
                        // Report only a portion if log is very large
                        progress?.Report(logContent.Length > 1000 
                            ? logContent.Substring(0, 1000) + "\n..." 
                            : logContent);
                        progress?.Report("--- SteamCMD Log File End ---");
                    } 
                    catch (Exception logEx) 
                    {
                        progress?.Report($"Warning: Could not read SteamCMD log file '{logPath}': {logEx.Message}");
                    }
                }

                return process.ExitCode == 0; // Success defined by exit code
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"Download failed: {ex.Message}");
                _dialogService.ShowError("Download Failed", $"An unexpected error occurred while running SteamCMD:\n\n{ex.Message}");
                return false;
            }
            finally
            {
                // Clean up script file
                if (File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); } 
                    catch { /* Ignore cleanup error */ }
                }
                
                // We keep the log file for debugging purposes
                // If you want to delete it, uncomment this code:
                /*
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } 
                    catch { /* Ignore cleanup error */ /*}
                }
                */
            }
        }
    }
}