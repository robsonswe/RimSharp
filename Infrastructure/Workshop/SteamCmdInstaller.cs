#nullable enable
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
        private readonly IPathService _gamePathService; // Keep game path service for checks
        private readonly IDialogService _dialogService;
        private readonly IHttpClientFactory _httpClientFactory;
        // Remove ISteamCmdFileSystem dependency
        // private readonly ISteamCmdFileSystem _fileSystem; 
        private readonly SteamCmdPlatformInfo _platformInfo;

        public SteamCmdInstaller(
            ISteamCmdPathService pathService,
            IPathService gamePathService,
            IDialogService dialogService,
            IHttpClientFactory httpClientFactory,
            // ISteamCmdFileSystem fileSystem, // Removed
            SteamCmdPlatformInfo platformInfo)
        {
            _pathService = pathService;
            _gamePathService = gamePathService; // Keep this
            _dialogService = dialogService;
            _httpClientFactory = httpClientFactory;
            // _fileSystem = fileSystem; // Removed
            _platformInfo = platformInfo;
        }

        public async Task<bool> CheckSetupAsync()
        {
            string? exePath = _pathService.SteamCmdExePath;
            if (string.IsNullOrEmpty(exePath))
                return false;

            // Add check for essential directories if needed, but exe check is primary
            if (!Directory.Exists(_pathService.SteamCmdSteamAppsPath))
            {
                // Maybe log a warning, but allow setup if exe exists
                 Console.WriteLine($"Warning: SteamCMD steamapps path does not exist: {_pathService.SteamCmdSteamAppsPath}");
            }

            return await Task.FromResult(File.Exists(exePath));
        }

        public async Task<bool> SetupAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!_platformInfo.IsSupported)
            {
                progress?.Report("Setup failed: Unsupported operating system.");
                _dialogService.ShowError("Setup Failed", "SteamCMD setup is not supported on this operating system.");
                return false;
            }

            // Keep check for mods path, as it's needed later for moving files
            if (string.IsNullOrWhiteSpace(_gamePathService.GetModsPath()))
            {
                progress?.Report("Setup failed: RimWorld Mods Path is not configured.");
                _dialogService.ShowError("Setup Failed", "Cannot set up SteamCMD because the RimWorld Mods Path is not configured in the application settings. This path is needed to move downloaded mods.");
                return false;
            }

            try
            {
                // --- 1. Ensure Directories Exist ---
                progress?.Report($"Ensuring SteamCMD directories exist in: {_pathService.SteamCmdPrefixPath}");
                Directory.CreateDirectory(_pathService.SteamCmdInstallPath);
                Directory.CreateDirectory(_pathService.SteamCmdSteamAppsPath); // Base 'steam' dir
                 // Ensure the specific workshop content parent exists for SteamCMD downloads
                string workshopContentParent = Path.GetDirectoryName(_pathService.SteamCmdWorkshopContentPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(workshopContentParent))
                {
                    Directory.CreateDirectory(workshopContentParent);
                    progress?.Report($"Ensured SteamCMD workshop content parent exists: {workshopContentParent}");
                }
                else
                {
                     progress?.Report($"Warning: Could not determine SteamCMD workshop content parent path from '{_pathService.SteamCmdWorkshopContentPath}'. Downloads might fail.");
                }


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
                try { File.Delete(tempArchivePath); } catch { /* Ignore */ }


                // --- 5. Symlink Setup --- REMOVED ---
                // No longer creating a symlink. Downloads will go to the actual SteamCMD path.


                // --- 6. Final Check ---
                bool setupComplete = await CheckSetupAsync();
                if (setupComplete)
                {
                    progress?.Report("SteamCMD setup successful (executable found).");
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
                    $"Permission denied during setup. Check permissions for the target directory: {_pathService.SteamCmdPrefixPath}\n\nError: {ex.Message}");
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

        // ExtractArchiveAsync and MakeExecutableAsync remain the same as before
        private async Task ExtractArchiveAsync(
            string archivePath,
            string destinationPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
             // Ensure destination exists, clear if necessary
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }
            // Optional: Clear existing content if a clean slate is desired
            // else {
            //     progress?.Report($"Clearing existing content in {destinationPath}...");
            //     try {
            //         foreach (var file in Directory.GetFiles(destinationPath)) File.Delete(file);
            //         foreach (var dir in Directory.GetDirectories(destinationPath)) Directory.Delete(dir, true);
            //     } catch (Exception ex) {
            //          progress?.Report($"Warning: Failed to clear existing content: {ex.Message}");
            //     }
            // }


            if (_platformInfo.IsArchiveZip)
            {
                try
                {
                    ZipFile.ExtractToDirectory(archivePath, destinationPath, true); // Overwrite files
                }
                catch (Exception ex)
                {
                     progress?.Report($"Error extracting ZIP: {ex.Message}");
                     throw; // Re-throw to indicate setup failure
                }
            }
            else // tar.gz - Using System.Formats.Tar (.NET 7+)
            {
                try
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
                catch (Exception ex)
                {
                     progress?.Report($"Error extracting TAR.GZ: {ex.Message}");
                     throw; // Re-throw to indicate setup failure
                }
            }
        }

        private async Task MakeExecutableAsync(string filePath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            // Check if chmod exists first? Maybe not necessary, assume standard POSIX environment.
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
                return; // Don't fail setup, but warn
            }

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                 string error = await process.StandardError.ReadToEndAsync();
                 progress?.Report($"Warning: Failed to make {Path.GetFileName(filePath)} executable (ExitCode: {process.ExitCode}). Downloads might fail. Error: {error}");
            }
            else
            {
                progress?.Report($"Made {Path.GetFileName(filePath)} executable.");
            }
        }
    }
}
