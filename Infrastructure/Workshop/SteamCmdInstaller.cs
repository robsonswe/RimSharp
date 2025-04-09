using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop
{
    /// <summary>
    /// Handles the installation and setup of SteamCMD.
    /// </summary>
    public class SteamCmdInstaller : ISteamCmdInstaller
    {
        private readonly ISteamCmdPathService _pathService;
        private readonly IPathService _gamePathService;
        private readonly IDialogService _dialogService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISteamCmdFileSystem _fileSystem;
        private readonly SteamCmdPlatformInfo _platformInfo;

        public SteamCmdInstaller(
            ISteamCmdPathService pathService, 
            IPathService gamePathService, 
            IDialogService dialogService,
            IHttpClientFactory httpClientFactory,
            ISteamCmdFileSystem fileSystem,
            SteamCmdPlatformInfo platformInfo)
        {
            _pathService = pathService;
            _gamePathService = gamePathService;
            _dialogService = dialogService;
            _httpClientFactory = httpClientFactory;
            _fileSystem = fileSystem;
            _platformInfo = platformInfo;
        }

        public async Task<bool> CheckSetupAsync()
        {
            string? exePath = _pathService.SteamCmdExePath;
            if (string.IsNullOrEmpty(exePath))
                return false;
                
            return File.Exists(exePath);
        }

        public async Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!_platformInfo.IsSupported)
            {
                progress?.Report("Setup failed: Unsupported operating system.");
                _dialogService.ShowError("Setup Failed", "SteamCMD setup is not supported on this operating system.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_gamePathService.GetModsPath()))
            {
                progress?.Report("Setup failed: RimWorld Mods Path is not configured.");
                _dialogService.ShowError("Setup Failed", "Cannot set up SteamCMD because the RimWorld Mods Path is not configured in the application settings.");
                return false;
            }

            try
            {
                // --- 1. Ensure Directories Exist ---
                progress?.Report($"Ensuring SteamCMD directories exist in: {_pathService.SteamCmdPrefixPath}");
                Directory.CreateDirectory(_pathService.SteamCmdInstallPath);
                Directory.CreateDirectory(_pathService.SteamCmdSteamAppsPath);
                Directory.CreateDirectory(Path.Combine(_pathService.SteamCmdSteamAppsPath, "steamapps", "workshop", "content")); // Parent for RW content

                cancellationToken.ThrowIfCancellationRequested();

                // --- 2. Download ---
                progress?.Report($"Downloading SteamCMD from {_platformInfo.SteamCmdUrl}...");
                string tempArchivePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_platformInfo.SteamCmdUrl));

                using (var client = _httpClientFactory.CreateClient())
                using (var response = await client.GetAsync(_platformInfo.SteamCmdUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    using (var fileStream = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        await httpStream.CopyToAsync(fileStream, cancellationToken);
                    }
                }
                progress?.Report("Download complete.");
                cancellationToken.ThrowIfCancellationRequested();

                // --- 3. Extract ---
                progress?.Report($"Extracting {Path.GetFileName(tempArchivePath)} to {_pathService.SteamCmdInstallPath}...");
                await ExtractArchiveAsync(tempArchivePath, _pathService.SteamCmdInstallPath, progress, cancellationToken);
                progress?.Report("Extraction complete.");
                cancellationToken.ThrowIfCancellationRequested();

                // --- 4. Delete Archive ---
                File.Delete(tempArchivePath);

                // --- 5. Symlink Setup ---
                string localModsPath = _gamePathService.GetModsPath(); // Source for the link
                string steamCmdWorkshopLinkPath = _pathService.SteamCmdWorkshopContentPath; // Destination for the link

                progress?.Report($"Configuring link/junction:");
                progress?.Report($"  Source (Your Mods): {localModsPath}");
                progress?.Report($"  Target (SteamCMD Downloads): {steamCmdWorkshopLinkPath}");

                bool linkCreated = await _fileSystem.CreateWorkshopLinkAsync(
                    localModsPath, 
                    steamCmdWorkshopLinkPath, 
                    progress, 
                    cancellationToken);
                    
                if (!linkCreated)
                {
                    progress?.Report("Failed to create the required link/junction. Downloads may not go to your mods folder.");
                    _dialogService.ShowWarning("Symlink Failed", 
                        $"Could not create the link/junction from:\n{steamCmdWorkshopLinkPath}\n\n" + 
                        $"To your mods folder:\n{localModsPath}\n\n" + 
                        "SteamCMD downloads might not appear in your configured mods folder. " + 
                        "This might require administrator privileges on Windows.");
                }
                else
                {
                    progress?.Report("Link/junction configuration successful.");
                }

                // --- 6. Final Check ---
                bool setupComplete = await CheckSetupAsync();
                if (setupComplete)
                {
                    progress?.Report("SteamCMD setup seems complete!");
                    return true;
                }
                else
                {
                    progress?.Report("Setup finished, but SteamCMD executable check failed.");
                    _dialogService.ShowError("Setup Failed", 
                        $"Setup process finished, but could not verify the SteamCMD executable at:\n{_pathService.SteamCmdExePath}");
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
                _dialogService.ShowError("Setup Failed", 
                    $"Permission denied during setup. Try running the application as administrator.\n\nError: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"Setup failed: {ex.Message}");
                _dialogService.ShowError("Setup Failed", 
                    $"An unexpected error occurred during SteamCMD setup:\n\n{ex.Message}");
                return false;
            }
            finally
            {
                // Clean up temp file if it still exists due to error
                string tempArchivePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_platformInfo.SteamCmdUrl));
                if (File.Exists(tempArchivePath))
                {
                    try { File.Delete(tempArchivePath); } catch { /* Ignore cleanup error */ }
                }
            }
        }

        private async Task ExtractArchiveAsync(
            string archivePath, 
            string destinationPath, 
            IProgress<string>? progress, 
            CancellationToken cancellationToken)
        {
            // Clear directory before extracting (doesn't always overwrite cleanly)
            if (Directory.Exists(destinationPath))
            {
                Directory.Delete(destinationPath, true);
                Directory.CreateDirectory(destinationPath);
            }

            if (_platformInfo.IsArchiveZip)
            {
                ZipFile.ExtractToDirectory(archivePath, destinationPath, true); // Overwrite files
            }
            else // tar.gz - Using System.Formats.Tar (.NET 7+)
            {
                using FileStream tarStream = File.OpenRead(archivePath);
                using var gzipStream = new System.IO.Compression.GZipStream(tarStream, CompressionMode.Decompress);
                await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gzipStream, destinationPath, true, cancellationToken);
                
                // Make steamcmd.sh executable on Linux/macOS
                if (_platformInfo.IsPosix && _pathService.SteamCmdExePath != null && File.Exists(_pathService.SteamCmdExePath))
                {
                    await MakeExecutableAsync(_pathService.SteamCmdExePath, progress, cancellationToken);
                }
            }
        }

        private async Task MakeExecutableAsync(string filePath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo("chmod", $"+x \"{filePath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null)
            {
                progress?.Report($"Warning: Failed to start chmod process for {filePath}");
                return;
            }
            
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                progress?.Report($"Warning: Failed to make {Path.GetFileName(filePath)} executable. Downloads might fail.");
            }
            else
            {
                progress?.Report($"Made {Path.GetFileName(filePath)} executable.");
            }
        }
    }
}