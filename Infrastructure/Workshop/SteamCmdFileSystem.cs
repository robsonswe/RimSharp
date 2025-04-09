#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Workshop
{
    /// <summary>
    /// Handles file system operations related to SteamCMD, such as
    /// creating symbolic links and managing the depot cache.
    /// </summary>
    public class SteamCmdFileSystem : ISteamCmdFileSystem
    {
        private readonly ISteamCmdPathService _pathService;
        private readonly SteamCmdPlatformInfo _platformInfo;

        public SteamCmdFileSystem(ISteamCmdPathService pathService, SteamCmdPlatformInfo platformInfo)
        {
            _pathService = pathService;
            _platformInfo = platformInfo;
        }

        public async Task<bool> CreateWorkshopLinkAsync(
            string sourceModsPath, 
            string targetLinkPath, 
            IProgress<string>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            // Ensure source exists (local mods path must be valid)
            if (!Directory.Exists(sourceModsPath))
            {
                progress?.Report($"Link Error: Source mods path does not exist: {sourceModsPath}");
                return false;
            }

            // Check if target already exists
            if (Directory.Exists(targetLinkPath) || File.Exists(targetLinkPath))
            {
                bool isLink = false;
                try { isLink = new FileInfo(targetLinkPath).LinkTarget != null; } catch {} 
                if (!isLink) 
                {
                    try { isLink = new DirectoryInfo(targetLinkPath).LinkTarget != null; } catch {}
                }

                // For junctions on Windows, checking LinkTarget might fail. Check attributes.
                if (!isLink && _platformInfo.Platform == OSPlatform.Windows)
                {
                    try
                    {
                        var attributes = File.GetAttributes(targetLinkPath);
                        isLink = attributes.HasFlag(FileAttributes.ReparsePoint);
                    } 
                    catch {}
                }

                if (isLink)
                {
                    progress?.Report($"Target path '{targetLinkPath}' already exists as a link/junction. Removing it first.");
                    try
                    {
                        Directory.Delete(targetLinkPath); // Deletes the link/junction, not the target content
                    } 
                    catch (IOException ex) 
                    {
                        progress?.Report($"Link Error: Could not remove existing link/junction. {ex.Message}");
                        return false;
                    }
                }
                else // It's a regular file or directory
                {
                    progress?.Report($"Target path '{targetLinkPath}' already exists and is NOT a link/junction.");
                    progress?.Report($"Attempting to delete existing file/directory at '{targetLinkPath}'...");
                    try
                    {
                        if (Directory.Exists(targetLinkPath))
                            Directory.Delete(targetLinkPath, true); // Recursive delete
                        else if (File.Exists(targetLinkPath))
                            File.Delete(targetLinkPath);
                    } 
                    catch (IOException ex) 
                    {
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
                // Try using Directory.CreateSymbolicLink first
                Directory.CreateSymbolicLink(targetLinkPath, sourceModsPath);

                // Verify creation
                await Task.Delay(100, cancellationToken); // Brief pause for FS changes
                if (Directory.Exists(targetLinkPath) && new DirectoryInfo(targetLinkPath).LinkTarget != null) 
                {
                    progress?.Report("Symbolic link created successfully.");
                    return true;
                } 
                else 
                {
                    // Fallback for Windows junction if symlink failed 
                    if (_platformInfo.Platform == OSPlatform.Windows)
                    {
                        if (Directory.Exists(targetLinkPath) && 
                            File.GetAttributes(targetLinkPath).HasFlag(FileAttributes.ReparsePoint))
                        {
                            progress?.Report("Junction appears to be created successfully.");
                            return true;
                        }
                        else 
                        {
                            // Try creating a Junction via mklink command
                            progress?.Report("Link/Junction creation check failed. Trying Junction via mklink...");
                            var mklinkPsi = new ProcessStartInfo("cmd.exe", 
                                $"/c mklink /J \"{targetLinkPath}\" \"{sourceModsPath}\"")
                            {
                                UseShellExecute = false, 
                                CreateNoWindow = true, 
                                RedirectStandardOutput = true, 
                                RedirectStandardError = true,
                            };
                            
                            using var mklinkProcess = Process.Start(mklinkPsi);
                            if (mklinkProcess == null)
                            {
                                progress?.Report("Failed to start mklink process.");
                                return false;
                            }
                            
                            await mklinkProcess.WaitForExitAsync(cancellationToken);
                            if (mklinkProcess.ExitCode == 0 && 
                                Directory.Exists(targetLinkPath) && 
                                File.GetAttributes(targetLinkPath).HasFlag(FileAttributes.ReparsePoint))
                            {
                                progress?.Report("Junction created successfully via mklink.");
                                return true;
                            } 
                            else 
                            {
                                string mklinkError = await mklinkProcess.StandardError.ReadToEndAsync();
                                string mklinkOut = await mklinkProcess.StandardOutput.ReadToEndAsync();
                                progress?.Report($"mklink failed (ExitCode {mklinkProcess.ExitCode}): {mklinkError} {mklinkOut}");
                                return false;
                            }
                        }
                    } 
                    else 
                    {
                        progress?.Report("Link creation failed (verification step).");
                        return false;
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                progress?.Report($"Link Error: Permission denied. Creating links often requires administrator privileges on Windows. {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"Link Error: An unexpected error occurred: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ClearDepotCacheAsync()
        {
            string cachePath = _pathService.SteamCmdDepotCachePath;
            if (string.IsNullOrEmpty(cachePath)) return false;

            if (!Directory.Exists(cachePath))
            {
                Console.WriteLine($"Depot cache directory not found, skipping clear: {cachePath}");
                return true; // Nothing to clear is success
            }

            Console.WriteLine($"Attempting to clear depot cache: {cachePath}");
            try
            {
                Directory.Delete(cachePath, true); // Recursive delete
                await Task.Delay(100); // Brief pause for FS
                return !Directory.Exists(cachePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to clear depot cache: {ex.Message}");
                return false;
            }
        }
    }
}