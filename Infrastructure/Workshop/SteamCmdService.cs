using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression; // For ZipFile
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;
// Add using for SharpCompress if you implement TAR.GZ
// using SharpCompress.Archives;
// using SharpCompress.Archives.Tar;
// using SharpCompress.Common;
// using SharpCompress.Readers;

namespace RimSharp.Infrastructure.Workshop
{
    public class SteamCmdService : ISteamCmdService
    {
        private const string SteamCmdPrefixConfigKey = "steamcmd_prefix";
        private const string RimworldAppId = "294100";

        private readonly IConfigService _configService;
        private readonly IPathService _pathService;
        private readonly IDialogService _dialogService;
        private readonly IHttpClientFactory _httpClientFactory;

        private string _steamCmdPrefixPath = string.Empty;
        private string _steamCmdInstallPath = string.Empty;
        private string _steamCmdSteamAppsPath = string.Empty; // Where downloads go
        private string _steamCmdWorkshopContentPath = string.Empty; // The specific content dir for RW
        private string _steamCmdDepotCachePath = string.Empty;
        private string? _steamCmdExePath;
        private bool _isSetupComplete = false;
        private readonly OSPlatform _currentPlatform;
        private readonly string _steamCmdUrl;
        private readonly string _steamCmdExeName;
        private readonly bool _isArchiveZip; // True for zip, false for tar.gz

        public string? SteamCmdExePath => _steamCmdExePath;
        public bool IsSetupComplete => _isSetupComplete;
        public string SteamCmdInstallPath => _steamCmdInstallPath;
        public string SteamCmdWorkshopContentPath => _steamCmdWorkshopContentPath;


        public SteamCmdService(IConfigService configService, IPathService pathService, IDialogService dialogService, IHttpClientFactory httpClientFactory)
        {
            _configService = configService;
            _pathService = pathService;
            _dialogService = dialogService;
            _httpClientFactory = httpClientFactory;

            // Determine OS and Set Platform Specifics
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _currentPlatform = OSPlatform.Windows;
                _steamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
                _steamCmdExeName = "steamcmd.exe";
                _isArchiveZip = true;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _currentPlatform = OSPlatform.Linux;
                _steamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
                _steamCmdExeName = "steamcmd.sh";
                _isArchiveZip = false; // Requires tar.gz handling
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _currentPlatform = OSPlatform.OSX;
                _steamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";
                _steamCmdExeName = "steamcmd.sh";
                _isArchiveZip = false; // Requires tar.gz handling
            }
            else
            {
                _currentPlatform = OSPlatform.FreeBSD; // Or some other default/unknown
                _steamCmdUrl = string.Empty;
                _steamCmdExeName = string.Empty;
                _isArchiveZip = false;
                _dialogService.ShowError("Unsupported Platform", "SteamCMD is not supported on this operating system.");
            }

            InitializePaths();
        }

        private void InitializePaths()
        {
            _steamCmdPrefixPath = _configService.GetConfigValue(SteamCmdPrefixConfigKey);
            if (string.IsNullOrWhiteSpace(_steamCmdPrefixPath))
            {
                // Default path (e.g., AppData or next to executable)
                string basePath = AppDomain.CurrentDomain.BaseDirectory; // Or Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                _steamCmdPrefixPath = Path.Combine(basePath, "SteamCMD_Data");
                // Don't save default automatically, wait for explicit setup/config change
            }

            // Define core paths based on the prefix
            _steamCmdInstallPath = Path.Combine(_steamCmdPrefixPath, "steamcmd");
            _steamCmdSteamAppsPath = Path.Combine(_steamCmdPrefixPath, "steam"); // Where 'force_install_dir' points
            _steamCmdWorkshopContentPath = Path.Combine(_steamCmdSteamAppsPath, "steamapps", "workshop", "content", RimworldAppId);
            _steamCmdDepotCachePath = Path.Combine(_steamCmdInstallPath, "depotcache");
            _steamCmdExePath = Path.Combine(_steamCmdInstallPath, _steamCmdExeName);

            // Initial check - don't block constructor
             _ = Task.Run(CheckSetupAsync); // Fire and forget initial check
        }

        public string GetSteamCmdPrefixPath() => _steamCmdPrefixPath;

        public async Task SetSteamCmdPrefixPathAsync(string prefixPath)
        {
            if (string.IsNullOrWhiteSpace(prefixPath)) return;
            _steamCmdPrefixPath = prefixPath;
            _configService.SetConfigValue(SteamCmdPrefixConfigKey, prefixPath);
            _configService.SaveConfig(); // Persist change
            InitializePaths(); // Re-calculate all derived paths
            await CheckSetupAsync(); // Re-check status with new path
        }


        public async Task<bool> CheckSetupAsync()
        {
            if (string.IsNullOrEmpty(_steamCmdExePath))
            {
                _isSetupComplete = false;
                return false;
            }
            _isSetupComplete = File.Exists(_steamCmdExePath);
            // Could add a check for the symlink existence here too if desired
            return _isSetupComplete;
        }

        public async Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (_currentPlatform == OSPlatform.FreeBSD || string.IsNullOrEmpty(_steamCmdUrl)) // Check for unsupported platform
            {
                progress?.Report("Setup failed: Unsupported operating system.");
                _dialogService.ShowError("Setup Failed", "SteamCMD setup is not supported on this operating system.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_pathService.GetModsPath()))
            {
                 progress?.Report("Setup failed: RimWorld Mods Path is not configured.");
                 _dialogService.ShowError("Setup Failed", "Cannot set up SteamCMD because the RimWorld Mods Path is not configured in the application settings.");
                 return false;
            }

            try
            {
                // --- 1. Ensure Directories Exist ---
                progress?.Report($"Ensuring SteamCMD directories exist in: {_steamCmdPrefixPath}");
                Directory.CreateDirectory(_steamCmdInstallPath);
                Directory.CreateDirectory(_steamCmdSteamAppsPath);
                Directory.CreateDirectory(Path.Combine(_steamCmdSteamAppsPath, "steamapps", "workshop", "content")); // Parent for RW content

                cancellationToken.ThrowIfCancellationRequested();

                // --- 2. Download ---
                progress?.Report($"Downloading SteamCMD from {_steamCmdUrl}...");
                string tempArchivePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_steamCmdUrl));

                using (var client = _httpClientFactory.CreateClient())
                using (var response = await client.GetAsync(_steamCmdUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    using (var fileStream = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken)) // NET 6+ use cancellationToken
                    {
                        await httpStream.CopyToAsync(fileStream, cancellationToken);
                    }
                }
                progress?.Report("Download complete.");
                cancellationToken.ThrowIfCancellationRequested();

                // --- 3. Extract ---
                progress?.Report($"Extracting {Path.GetFileName(tempArchivePath)} to {_steamCmdInstallPath}...");
                if (_isArchiveZip)
                {
                    // Clear directory before extracting ZIP (ZipFile doesn't overwrite cleanly sometimes)
                    if(Directory.Exists(_steamCmdInstallPath))
                    {
                        Directory.Delete(_steamCmdInstallPath, true);
                        Directory.CreateDirectory(_steamCmdInstallPath);
                    }
                    ZipFile.ExtractToDirectory(tempArchivePath, _steamCmdInstallPath, true); // Overwrite files
                }
                else // tar.gz - Requires SharpCompress or System.Formats.Tar (.NET 7+)
                {
                    // Example using System.Formats.Tar (needs .NET 7+)
                    // Ensure directory is clean for TAR extraction
                     if(Directory.Exists(_steamCmdInstallPath))
                    {
                        Directory.Delete(_steamCmdInstallPath, true);
                        Directory.CreateDirectory(_steamCmdInstallPath);
                    }
                    using FileStream tarStream = File.OpenRead(tempArchivePath);
                    using var gzipStream = new System.IO.Compression.GZipStream(tarStream, CompressionMode.Decompress);
                    await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gzipStream, _steamCmdInstallPath, true, cancellationToken);


                    // // Example using SharpCompress (Needs NuGet package SharpCompress)
                    // using (var archive = ArchiveFactory.Open(tempArchivePath))
                    // {
                    //     foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    //     {
                    //         // Ensure paths are relative within the install dir
                    //         // SharpCompress might extract with full paths sometimes? Needs testing.
                    //         entry.WriteToDirectory(_steamCmdInstallPath, new ExtractionOptions()
                    //         {
                    //             ExtractFullPath = true,
                    //             Overwrite = true
                    //         });
                    //     }
                    // }
                    progress?.Report("Extraction complete (using System.Formats.Tar).");

                     // Make steamcmd.sh executable on Linux/macOS
                    if ((_currentPlatform == OSPlatform.Linux || _currentPlatform == OSPlatform.OSX) && !string.IsNullOrEmpty(_steamCmdExePath))
                    {
                         if (File.Exists(_steamCmdExePath))
                         {
                              var psi = new ProcessStartInfo("chmod", $"+x \"{_steamCmdExePath}\"")
                              {
                                   UseShellExecute = false,
                                   RedirectStandardOutput = true,
                                   RedirectStandardError = true,
                                   CreateNoWindow = true
                              };
                              using var process = Process.Start(psi);
                              await process.WaitForExitAsync(cancellationToken);
                              if(process.ExitCode != 0)
                              {
                                  progress?.Report($"Warning: Failed to make {_steamCmdExeName} executable. Downloads might fail.");
                              } else {
                                  progress?.Report($"Made {_steamCmdExeName} executable.");
                              }
                         }
                    }

                }
                progress?.Report("Extraction complete.");
                cancellationToken.ThrowIfCancellationRequested();


                // --- 4. Delete Archive ---
                File.Delete(tempArchivePath);

                // --- 5. Symlink Setup ---
                string localModsPath = _pathService.GetModsPath(); // Source for the link
                string steamCmdWorkshopLinkPath = _steamCmdWorkshopContentPath; // Destination for the link

                progress?.Report($"Configuring link/junction:");
                progress?.Report($"  Source (Your Mods): {localModsPath}");
                progress?.Report($"  Target (SteamCMD Downloads): {steamCmdWorkshopLinkPath}");

                bool linkCreated = await CreateWorkshopLinkAsync(localModsPath, steamCmdWorkshopLinkPath, progress, cancellationToken);
                 if (!linkCreated)
                {
                    progress?.Report("Failed to create the required link/junction. Downloads may not go to your mods folder.");
                    // Don't fail the whole setup, but warn the user
                     _dialogService.ShowWarning("Symlink Failed", $"Could not create the link/junction from:\n{steamCmdWorkshopLinkPath}\n\nTo your mods folder:\n{localModsPath}\n\nSteamCMD downloads might not appear in your configured mods folder. This might require administrator privileges on Windows.");

                } else {
                     progress?.Report("Link/junction configuration successful.");
                }


                // --- 6. Final Check & Save Config ---
                await CheckSetupAsync(); // Update the IsSetupComplete flag
                 if (_isSetupComplete)
                 {
                     // Save the prefix path used, especially if it was the default
                     await SetSteamCmdPrefixPathAsync(_steamCmdPrefixPath);
                     progress?.Report("SteamCMD setup seems complete!");
                     return true;
                 }
                 else
                 {
                     progress?.Report("Setup finished, but SteamCMD executable check failed.");
                     _dialogService.ShowError("Setup Failed", $"Setup process finished, but could not verify the SteamCMD executable at:\n{_steamCmdExePath}");
                     return false;
                 }
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Setup cancelled.");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                 progress?.Report($"Setup failed: Permission denied. {ex.Message}");
                 _dialogService.ShowError("Setup Failed", $"Permission denied during setup. Try running the application as administrator.\n\nError: {ex.Message}");
                 return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"Setup failed: {ex.Message}");
                // Log full exception details somewhere persistent if possible
                _dialogService.ShowError("Setup Failed", $"An unexpected error occurred during SteamCMD setup:\n\n{ex.Message}");
                return false;
            }
            finally
            {
                // Clean up temp file if it still exists due to error
                string tempArchivePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_steamCmdUrl));
                if(File.Exists(tempArchivePath))
                {
                    try { File.Delete(tempArchivePath); } catch { /* Ignore cleanup error */ }
                }
            }
        }

        private async Task<bool> CreateWorkshopLinkAsync(string sourceModsPath, string targetLinkPath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            // Ensure source exists (local mods path must be valid)
            if (!Directory.Exists(sourceModsPath))
            {
                progress?.Report($"Link Error: Source mods path does not exist: {sourceModsPath}");
                return false;
            }

            // Check existing target
            if (Directory.Exists(targetLinkPath) || File.Exists(targetLinkPath))
            {
                bool isLink = false;
                try { isLink = new FileInfo(targetLinkPath).LinkTarget != null; } catch {} // Check if it's a symlink
                 if (!isLink) {
                     try { isLink = new DirectoryInfo(targetLinkPath).LinkTarget != null; } catch { }
                 }

                 // For junctions on Windows, checking LinkTarget might fail. Check attributes.
                 if (!isLink && _currentPlatform == OSPlatform.Windows)
                 {
                    try
                    {
                        var attributes = File.GetAttributes(targetLinkPath);
                        isLink = attributes.HasFlag(FileAttributes.ReparsePoint);
                    } catch {}
                 }


                if (isLink)
                {
                     progress?.Report($"Target path '{targetLinkPath}' already exists as a link/junction. Removing it first.");
                     try
                     {
                          Directory.Delete(targetLinkPath); // Deletes the link/junction, not the target content
                     } catch (IOException ex) {
                          progress?.Report($"Link Error: Could not remove existing link/junction. {ex.Message}");
                          return false;
                     }
                }
                else // It's a regular file or directory
                {
                     progress?.Report($"Target path '{targetLinkPath}' already exists and is NOT a link/junction.");
                     // Ask user? Or just delete? Forcing delete for now, similar to Python 'force'.
                     // Could use _dialogService.ShowConfirmation here if needed.
                     progress?.Report($"Attempting to delete existing file/directory at '{targetLinkPath}'...");
                     try
                     {
                          if(Directory.Exists(targetLinkPath))
                             Directory.Delete(targetLinkPath, true); // Recursive delete
                          else if (File.Exists(targetLinkPath))
                             File.Delete(targetLinkPath);
                     } catch (IOException ex) {
                          progress?.Report($"Link Error: Could not remove existing file/directory. {ex.Message}");
                          return false;
                     }
                }
            }

            // Ensure parent directory of the target exists
             string? parentDir = Path.GetDirectoryName(targetLinkPath);
             if (parentDir != null && !Directory.Exists(parentDir))
             {
                 Directory.CreateDirectory(parentDir);
             }


            // Create the link/junction
            progress?.Report($"Attempting to create link/junction from '{targetLinkPath}' -> '{sourceModsPath}'");
            try
            {
                 // Use Directory.CreateSymbolicLink for cross-platform compatibility (.NET Core 3+)
                 // Note: May require admin rights on Windows depending on configuration/version.
                 Directory.CreateSymbolicLink(targetLinkPath, sourceModsPath);

                 // Verify creation
                 await Task.Delay(100, cancellationToken); // Brief pause for FS changes
                 if (Directory.Exists(targetLinkPath) && new DirectoryInfo(targetLinkPath).LinkTarget != null) {
                     progress?.Report("Symbolic link created successfully.");
                     return true;
                 } else {
                    // Fallback/Check for Junction on Windows if symlink failed or check is unreliable
                    if (_currentPlatform == OSPlatform.Windows)
                    {
                        if(Directory.Exists(targetLinkPath) && File.GetAttributes(targetLinkPath).HasFlag(FileAttributes.ReparsePoint))
                        {
                            progress?.Report("Junction appears to be created successfully.");
                            return true;
                        }
                        else {
                            // Try creating a Junction explicitly as a fallback? (Requires P/Invoke or Process Start)
                             progress?.Report("Link/Junction creation check failed. Trying Junction via mklink...");
                             var mklinkPsi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{targetLinkPath}\" \"{sourceModsPath}\"")
                             {
                                  UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                             };
                             using var mklinkProcess = Process.Start(mklinkPsi);
                             await mklinkProcess.WaitForExitAsync(cancellationToken);
                              if (mklinkProcess.ExitCode == 0 && Directory.Exists(targetLinkPath) && File.GetAttributes(targetLinkPath).HasFlag(FileAttributes.ReparsePoint))
                              {
                                    progress?.Report("Junction created successfully via mklink.");
                                    return true;
                              } else {
                                    string mklinkError = await mklinkProcess.StandardError.ReadToEndAsync();
                                    string mklinkOut = await mklinkProcess.StandardOutput.ReadToEndAsync();
                                    progress?.Report($"mklink failed (ExitCode {mklinkProcess.ExitCode}): {mklinkError} {mklinkOut}");
                                    return false;
                              }
                        }
                    } else {
                         progress?.Report("Link creation failed (verification step).");
                         return false;
                    }
                 }
            }
            catch (UnauthorizedAccessException ex)
            {
                progress?.Report($"Link Error: Permission denied. Creating links often requires administrator privileges on Windows. {ex.Message}");
                // Don't show dialog here, let the caller handle overall setup failure warning.
                return false;
            }
            catch (Exception ex)
            {
                 progress?.Report($"Link Error: An unexpected error occurred: {ex.Message}");
                 return false;
            }
        }


        public async Task<bool> DownloadModsAsync(IEnumerable<string> workshopIds, bool validate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!_isSetupComplete || string.IsNullOrEmpty(_steamCmdExePath))
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
                scriptBuilder.AppendLine($"force_install_dir \"{_steamCmdSteamAppsPath}\"");
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
                    } else {
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
                progress?.Report($"Executing SteamCMD: {_steamCmdExePath} +runscript \"{scriptPath}\"");

                // Delete old log file if it exists
                if (File.Exists(logPath)) File.Delete(logPath);

                 var processStartInfo = new ProcessStartInfo
                 {
                    FileName = _steamCmdExePath,
                    Arguments = $"+runscript \"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true, // Capture stdout
                    RedirectStandardError = true,  // Capture stderr
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8, // Match SteamCMD output if needed
                    StandardErrorEncoding = Encoding.UTF8
                 };


                using var process = new Process { StartInfo = processStartInfo };

                // Handle output data received events to report progress
                process.OutputDataReceived += (sender, args) => { if (args.Data != null) progress?.Report($"SteamCMD: {args.Data}"); };
                process.ErrorDataReceived += (sender, args) => { if (args.Data != null) progress?.Report($"SteamCMD ERR: {args.Data}"); };


                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for exit or cancellation
                await process.WaitForExitAsync(cancellationToken);

                progress?.Report($"SteamCMD process exited with code: {process.ExitCode}");

                 // Read the full log file after completion for detailed info
                 if(File.Exists(logPath))
                 {
                      try {
                          string logContent = await File.ReadAllTextAsync(logPath, cancellationToken);
                          progress?.Report("--- SteamCMD Log File Start ---");
                          // Report log line by line might flood the progress reporter
                          // Report first/last N lines or specific patterns?
                          progress?.Report(logContent.Length > 1000 ? logContent.Substring(0, 1000) + "\n..." : logContent);
                          progress?.Report("--- SteamCMD Log File End ---");
                      } catch (Exception logEx) {
                          progress?.Report($"Warning: Could not read SteamCMD log file '{logPath}': {logEx.Message}");
                      }
                 }


                return process.ExitCode == 0; // Success defined by exit code
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Download cancelled.");
                // Attempt to kill process? Might be risky.
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
                    try { File.Delete(scriptPath); } catch { /* Ignore cleanup error */ }
                }
                 // Optionally delete log file or keep it for debugging
                 // if (File.Exists(logPath))
                 // {
                 //     try { File.Delete(logPath); } catch { /* Ignore cleanup error */ }
                 // }
            }
        }

        public async Task<bool> ClearDepotCacheAsync()
        {
             if (string.IsNullOrEmpty(_steamCmdDepotCachePath)) return false;

             if (!Directory.Exists(_steamCmdDepotCachePath))
             {
                 Console.WriteLine($"Depot cache directory not found, skipping clear: {_steamCmdDepotCachePath}");
                 return true; // Nothing to clear is success
             }

             Console.WriteLine($"Attempting to clear depot cache: {_steamCmdDepotCachePath}");
             try
             {
                 Directory.Delete(_steamCmdDepotCachePath, true); // Recursive delete
                 await Task.Delay(100); // Brief pause for FS
                 return !Directory.Exists(_steamCmdDepotCachePath);
             }
             catch (Exception ex)
             {
                 Console.WriteLine($"Failed to clear depot cache: {ex.Message}");
                  _dialogService.ShowWarning("Clear Cache Failed", $"Could not delete the SteamCMD depot cache:\n{_steamCmdDepotCachePath}\n\nError: {ex.Message}");
                 return false;
             }
        }
    }
}
