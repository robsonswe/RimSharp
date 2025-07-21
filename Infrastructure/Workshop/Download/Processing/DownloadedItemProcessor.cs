#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; // For DownloadItem
using RimSharp.Shared.Services.Contracts; // For ILoggerService, IModService

namespace RimSharp.Infrastructure.Workshop.Download.Processing
{
    public class DownloadedItemProcessor : IDownloadedItemProcessor
    {
        private readonly ILoggerService _logger;
        private readonly IModService _modService;
        private readonly List<string> _logMessages = new List<string>();

        public DownloadedItemProcessor(ILoggerService logger, IModService modService)
        {
            _logger = logger;
            _modService = modService;
        }

        public List<string> GetLogMessages() => new List<string>(_logMessages);
        public void AddLogMessage(string message) => _logMessages.Add(message);

        public async Task<bool> ProcessItemAsync(
            DownloadItem item,
            string sourcePath,
            string targetPath,
            string backupSuffix,
            CancellationToken cancellationToken)
        {
            _logMessages.Clear();
            string itemId = item.SteamId ?? "UNKNOWN_ID";
            string itemName = item.Name ?? $"Item {itemId}";

            // State variables for robust cleanup and rollback
            string? stagingPath = null;
            string? backupPath = null;
            bool backupCreated = false;
            string finalSourcePath = sourcePath;

            _logger.LogInfo($"Processing item {itemId} ('{itemName}'): Source='{sourcePath}', Target='{targetPath}'", "DownloadedItemProcessor");
            AddLogMessage($"Processing item {itemId} ({itemName})...");

            try
            {
                // --- Step 1: Verify and Prepare Original Source ---
                if (!Directory.Exists(sourcePath))
                {
                    _logger.LogError($"Item {itemId}: Source directory '{sourcePath}' not found. Cannot process.", "DownloadedItemProcessor");
                    AddLogMessage($"Error: Item {itemId} source files not found. Download may have failed.");
                    return false;
                }

                await _modService.CreateTimestampFilesAsync(sourcePath, itemId, item.PublishDate ?? "", item.StandardDate ?? "");
                _logger.LogInfo($"Item {itemId}: Timestamp files created in original source.", "DownloadedItemProcessor");
                cancellationToken.ThrowIfCancellationRequested();


                // --- Step 2: Handle Cross-Volume "Staging" to Guarantee Atomic Swap ---
                if (!AreOnSameVolume(sourcePath, targetPath))
                {
                    _logger.LogWarning($"Item {itemId}: Cross-volume operation detected. Staging files on target volume for atomic move.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId}: Cross-drive operation. Staging files for safety...");

                    string targetParentDir = Path.GetDirectoryName(targetPath) ?? throw new DirectoryNotFoundException("Could not determine target parent directory.");
                    stagingPath = Path.Combine(targetParentDir, $"{itemId}_staging_{Guid.NewGuid():N}");

                    AddLogMessage($"Item {itemId}: Copying to staging area (this may take a moment)...");
                    await CopyDirectoryRecursivelyAsync(sourcePath, stagingPath, cancellationToken);
                    _logger.LogInfo($"Item {itemId}: Staging copy complete to '{stagingPath}'.", "DownloadedItemProcessor");

                    Directory.Delete(sourcePath, true);
                    finalSourcePath = stagingPath;
                }
                cancellationToken.ThrowIfCancellationRequested();


                // --- Step 3: Perform the Atomic "Backup and Swap" ---
                if (Directory.Exists(targetPath))
                {
                    backupPath = $"{targetPath}{backupSuffix}_{Guid.NewGuid():N}";
                    _logger.LogInfo($"Item {itemId}: Backing up existing installation to '{backupPath}'.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId}: Found existing version. Creating unique backup...");

                    Directory.Move(targetPath, backupPath);
                    if (!Directory.Exists(backupPath) || Directory.Exists(targetPath))
                        throw new IOException($"Verification failed after moving target to backup path '{backupPath}'.");

                    backupCreated = true;
                }

                _logger.LogInfo($"Item {itemId}: Performing atomic move from '{finalSourcePath}' to '{targetPath}'.", "DownloadedItemProcessor");
                AddLogMessage($"Item {itemId}: Activating new version...");
                Directory.Move(finalSourcePath, targetPath);
                if (!Directory.Exists(targetPath) || Directory.Exists(finalSourcePath))
                    throw new IOException($"Verification failed after moving staged version to target path '{targetPath}'.");


                // --- Step 4: Success! Preserve DDS files and then clean up the backup ---
                if (backupCreated && !string.IsNullOrEmpty(backupPath))
                {
                    // ---vvv--- NEW DDS PRESERVATION LOGIC ---vvv---
                    _logger.LogInfo($"Item {itemId}: Scanning for custom DDS files to preserve from backup '{backupPath}'.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId}: Checking for custom DDS files to preserve...");
                    await PreserveDdsFilesAsync(backupPath, targetPath, itemId, cancellationToken);
                    // ---^^^--- END OF NEW LOGIC ---^^^---

                    _logger.LogInfo($"Item {itemId}: Update complete. Deleting temporary backup '{backupPath}'.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId}: Removing temporary backup...");
                    Directory.Delete(backupPath, true);
                }

                _logger.LogInfo($"Item {itemId} ('{itemName}') processing SUCCEEDED.", "DownloadedItemProcessor");
                AddLogMessage($"Item {itemId} ({itemName}) successfully updated/installed.");
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError($"Item {itemId}: FAILED during update process. Error: {ex.Message}", "DownloadedItemProcessor");
                AddLogMessage($"Error processing item {itemId}: {ex.Message}. Attempting to rollback...");

                if (backupCreated && !string.IsNullOrEmpty(backupPath))
                {
                    bool rollbackSuccess = await AttemptRollbackAsync(targetPath, backupPath, itemId, CancellationToken.None);
                    AddLogMessage(rollbackSuccess
                        ? $"Item {itemId}: Successfully rolled back to the previous version."
                        : $"CRITICAL: Item {itemId} FAILED TO ROLLBACK. The mod may be broken. Manual intervention required.");
                }
                return false;
            }
            finally
            {
                // --- Guaranteed Cleanup ---
                // This block ensures that if the process fails or is cancelled after staging, we don't leave junk on the user's disk.
                if (stagingPath != null && Directory.Exists(stagingPath))
                {
                    try
                    {
                        _logger.LogWarning($"Item {itemId}: Cleaning up leftover staging directory '{stagingPath}' after process termination.", "DownloadedItemProcessor");
                        Directory.Delete(stagingPath, true);
                    }
                    catch (Exception cleanupEx)
                    {
                        // Log this as a critical failure, as it means we've leaked files onto the disk.
                        _logger.LogCritical($"Item {itemId}: FAILED TO CLEANUP STAGING DIRECTORY '{stagingPath}'. Manual deletion may be required. Error: {cleanupEx.Message}", "DownloadedItemProcessor");
                    }
                }
            }
        }

        /// <summary>
        /// Preserves custom .dds files from a backup directory if their .png counterpart
        /// exists and is unchanged in the new mod directory.
        /// </summary>
        private async Task PreserveDdsFilesAsync(string backupPath, string newModPath, string itemId, CancellationToken cancellationToken)
        {
            int preservedCount = 0;
            try
            {
                var ddsFiles = Directory.EnumerateFiles(backupPath, "*.dds", SearchOption.AllDirectories);

                foreach (var backupDdsPath in ddsFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string relativePath = Path.GetRelativePath(backupPath, backupDdsPath);
                    string newDdsPath = Path.Combine(newModPath, relativePath);

                    // ---vvv---  RULE #0  ---vvv---
                    // If a DDS file with this name already exists in the new version, it's an
                    // official file from the mod author. We must not touch it.
                    if (File.Exists(newDdsPath))
                    {
                        _logger.LogDebug($"Item {itemId}: Skipping DDS '{relativePath}'. It is an official file included in the new version.", "DownloadedItemProcessor");
                        continue;
                    }

                    string pngRelativePath = Path.ChangeExtension(relativePath, ".png");

                    string backupPngPath = Path.Combine(backupPath, pngRelativePath);
                    string newPngPath = Path.Combine(newModPath, pngRelativePath);

                    // Rule 1: PNG counterpart must exist in the new mod folder.
                    if (!File.Exists(newPngPath))
                    {
                        _logger.LogDebug($"Item {itemId}: Skipping DDS '{relativePath}'. Its PNG counterpart was removed in the new version.", "DownloadedItemProcessor");
                        continue;
                    }

                    // Sanity check: ensure the PNG also existed in the backup.
                    if (!File.Exists(backupPngPath))
                    {
                        _logger.LogWarning($"Item {itemId}: Skipping DDS '{relativePath}'. Found DDS in backup without its PNG counterpart.", "DownloadedItemProcessor");
                        continue;
                    }

                    // Rule 2: Compare hashes of old and new PNGs to ensure it wasn't modified.
                    string oldPngHash = await ComputeFileHashAsync(backupPngPath, cancellationToken);
                    string newPngHash = await ComputeFileHashAsync(newPngPath, cancellationToken);

                    if (oldPngHash != newPngHash)
                    {
                        _logger.LogDebug($"Item {itemId}: Discarding DDS '{relativePath}'. Its PNG counterpart was modified in the update.", "DownloadedItemProcessor");
                        continue;
                    }

                    // All rules passed. This is a custom DDS file that can be safely moved.
                    string? destDir = Path.GetDirectoryName(newDdsPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);

                    File.Move(backupDdsPath, newDdsPath, true);
                    _logger.LogInfo($"Item {itemId}: Preserved custom DDS file: '{relativePath}'", "DownloadedItemProcessor");
                    preservedCount++;
                }

                if (preservedCount > 0)
                {
                    AddLogMessage($"Item {itemId}: Successfully preserved {preservedCount} custom DDS file(s).");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError($"Item {itemId}: An unexpected error occurred during DDS preservation: {ex.Message}", "DownloadedItemProcessor");
                AddLogMessage($"Warning: Could not preserve some DDS files due to an error: {ex.Message}");
            }
        }


        private async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            using (var sha256 = SHA256.Create())
            {
                // Use an async-compatible file stream opening
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        private Task<bool> AttemptRollbackAsync(string targetPath, string backupPath, string itemId, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                _logger.LogWarning($"Item {itemId}: An error occurred. Attempting to rollback from '{backupPath}' to '{targetPath}'.", "DownloadedItemProcessor");
                try
                {
                    if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, true);
                    }
                    Directory.Move(backupPath, targetPath);
                    if (!Directory.Exists(targetPath) || Directory.Exists(backupPath))
                        throw new IOException("Verification failed after restoring backup.");

                    _logger.LogInfo($"Item {itemId}: Rollback successful. Previous version has been restored.", "DownloadedItemProcessor");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"Item {itemId}: CRITICAL ROLLBACK FAILURE. Could not restore backup '{backupPath}'. The mod is likely broken or missing. Error: {ex.Message}", "DownloadedItemProcessor");
                    return false;
                }
            }, cancellationToken);
        }

        private bool AreOnSameVolume(string path1, string path2)
        {
            string? root1 = Path.GetPathRoot(Path.GetFullPath(path1));
            string? root2 = Path.GetPathRoot(Path.GetFullPath(path2));

            if (string.IsNullOrEmpty(root1) || string.IsNullOrEmpty(root2))
            {
                _logger.LogWarning($"Could not determine volume for one or both paths ('{path1}', '{path2}'). Assuming different volumes for safety.", "DownloadedItemProcessor");
                return false;
            }

            return string.Equals(root1, root2, StringComparison.OrdinalIgnoreCase);
        }

        private async Task CopyDirectoryRecursivelyAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken)
        {
            var dir = new DirectoryInfo(sourceDir);
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, file.Name);
                await CopyFileWithRetryAsync(file, tempPath, cancellationToken);
            }

            foreach (DirectoryInfo subdir in dir.GetDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, subdir.Name);
                await CopyDirectoryRecursivelyAsync(subdir.FullName, tempPath, cancellationToken);
            }
        }

        private async Task CopyFileWithRetryAsync(FileInfo sourceFile, string destFileName, CancellationToken cancellationToken)
        {
            const int maxRetries = 2;
            const int retryDelayMs = 250;
            for (int i = 0; i <= maxRetries; i++)
            {
                try { sourceFile.CopyTo(destFileName, true); return; }
                catch (IOException) when (i < maxRetries) { await Task.Delay(retryDelayMs, cancellationToken); }
            }
        }
    }
}