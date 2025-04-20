#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models; // For DownloadItem
using RimSharp.Shared.Services.Contracts; // For ILoggerService, IModService

namespace RimSharp.Infrastructure.Workshop.Download.Processing
{
    public class DownloadedItemProcessor : IDownloadedItemProcessor
    {
        private readonly ILoggerService _logger;
        private readonly IModService _modService; // For creating timestamp files
        private readonly List<string> _logMessages = new List<string>(); // Internal logging buffer

        public DownloadedItemProcessor(ILoggerService logger, IModService modService)
        {
            _logger = logger;
            _modService = modService;
        }

        public async Task<bool> ProcessItemAsync(
            DownloadItem item,
            string sourcePath,
            string targetPath,
            string backupSuffix,
            CancellationToken cancellationToken)
        {
             _logMessages.Clear(); // Clear previous item's messages
            string itemId = item.SteamId ?? "UNKNOWN_ID";
            string itemName = item.Name ?? $"Item {itemId}";
            string backupPath = targetPath + backupSuffix;
            bool backupCreated = false;
            bool moveOrCopyAttempted = false;

            _logger.LogInfo($"Processing item {itemId} ('{itemName}'): Source='{sourcePath}', Target='{targetPath}'", "DownloadedItemProcessor");
            AddLogMessage($"Processing item {itemId} ({itemName})...");


            // Step 1: Verify Download Files Exist at Source
            if (!Directory.Exists(sourcePath))
            {
                _logger.LogError($"Item {itemId}: Source directory '{sourcePath}' not found. Cannot process.", "DownloadedItemProcessor");
                AddLogMessage($"Error: Item {itemId} ({itemName}) source files not found at '{sourcePath}'.");
                return false;
            }
             _logger.LogDebug($"Item {itemId}: Source directory found.", "DownloadedItemProcessor");

            try
            {
                // Step 2: Create Timestamps in Source
                _logger.LogDebug($"Item {itemId}: Creating timestamp files in source path '{sourcePath}'", "DownloadedItemProcessor");
                string pubDate = item.PublishDate ?? string.Empty;
                string stdDate = item.StandardDate ?? string.Empty;
                try
                {
                    await _modService.CreateTimestampFilesAsync(sourcePath, itemId, pubDate, stdDate);
                    _logger.LogInfo($"Item {itemId}: Timestamp files created successfully in source.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId}: Timestamp files created.");
                }
                catch(Exception tsEx)
                {
                     _logger.LogError($"Item {itemId}: FAILED to create timestamp files in '{sourcePath}'. Error: {tsEx.Message}", "DownloadedItemProcessor");
                     AddLogMessage($"Error: Item {itemId} failed creating timestamp files: {tsEx.Message}");
                     throw; // Fail the whole process for this item
                }


                cancellationToken.ThrowIfCancellationRequested();

                // --- Step 3: Prepare Target & Create Backup ---
                if (Directory.Exists(targetPath))
                {
                    _logger.LogInfo($"Item {itemId}: Existing directory found at target path '{targetPath}'. Attempting backup.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId}: Found existing version. Creating backup...");

                    // 3a. Delete lingering backup
                    if (Directory.Exists(backupPath))
                    {
                        _logger.LogWarning($"Item {itemId}: Found lingering backup directory '{backupPath}'. Deleting it...", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Cleaning up old backup...");
                        try
                        {
                            Directory.Delete(backupPath, true);
                            await Task.Delay(50, cancellationToken); // Brief pause
                            if (Directory.Exists(backupPath)) throw new IOException("Lingering backup directory still exists after delete attempt.");
                            _logger.LogDebug($"Item {itemId}: Lingering backup deleted.", "DownloadedItemProcessor");
                        }
                        catch (Exception bkDelEx)
                        {
                            _logger.LogError($"Item {itemId}: FAILED to delete lingering backup '{backupPath}'. Error: {bkDelEx.Message}. Aborting update for this item.", "DownloadedItemProcessor");
                            AddLogMessage($"Error: Item {itemId} failed to clean up old backup '{Path.GetFileName(backupPath)}'. Update aborted.");
                            throw new IOException($"Failed to delete lingering backup '{backupPath}'.", bkDelEx); // Propagate to outer catch
                        }
                    }

                    // 3b. Rename current target to backup
                    try
                    {
                        _logger.LogDebug($"Item {itemId}: Renaming '{targetPath}' to '{backupPath}'", "DownloadedItemProcessor");
                        Directory.Move(targetPath, backupPath);
                        backupCreated = true;
                        await Task.Delay(50, cancellationToken); // Brief pause
                        if (!Directory.Exists(backupPath) || Directory.Exists(targetPath)) throw new IOException("Backup rename verification failed.");
                        _logger.LogInfo($"Item {itemId}: Backup created: '{backupPath}'.", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Backup created as '{Path.GetFileName(backupPath)}'.");
                    }
                    catch (Exception rnEx)
                    {
                        _logger.LogError($"Item {itemId}: FAILED to rename existing '{targetPath}' to '{backupPath}'. Error: {rnEx.Message}. Aborting update.", "DownloadedItemProcessor");
                        AddLogMessage($"Error: Item {itemId} failed to create backup from '{Path.GetFileName(targetPath)}'. Update aborted.");
                        throw new IOException($"Failed to rename '{targetPath}' to '{backupPath}' for backup.", rnEx); // Propagate
                    }
                }
                else
                {
                    _logger.LogDebug($"Item {itemId}: No existing directory at target '{targetPath}'. No backup needed.", "DownloadedItemProcessor");
                }

                cancellationToken.ThrowIfCancellationRequested();

                 // --- Step 4: Move/Copy Source to Target ---
                moveOrCopyAttempted = true;
                try
                {
                    string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
                    string? targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));
                    bool crossVolume = sourceRoot != null && targetRoot != null &&
                                       !string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);

                    if (crossVolume)
                    {
                        _logger.LogInfo($"Item {itemId}: Cross-volume copy required: '{sourcePath}' -> '{targetPath}'", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Copying new version (cross-volume)...");
                        await CopyDirectoryRecursivelyAsync(sourcePath, targetPath, cancellationToken);
                        _logger.LogInfo($"Item {itemId}: Copy complete. Verifying and deleting source.", "DownloadedItemProcessor");
                         // Verify copy seems okay before deleting source
                        if (!Directory.Exists(targetPath)) throw new IOException($"Cross-volume copy failed: Target directory '{targetPath}' not found after copy.");
                        // Add more verification? File count? Size?
                        Directory.Delete(sourcePath, true);
                        _logger.LogDebug($"Item {itemId}: Source directory deleted after copy.", "DownloadedItemProcessor");
                    }
                    else
                    {
                        _logger.LogInfo($"Item {itemId}: Moving: '{sourcePath}' -> '{targetPath}' (same volume)", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Moving new version...");
                        Directory.Move(sourcePath, targetPath);
                    }

                    await Task.Delay(50, cancellationToken); // Brief pause for FS

                    // --- Step 5: Verify Move/Copy Outcome ---
                    if (!Directory.Exists(targetPath)) // Basic check: target must exist
                    {
                         throw new IOException($"Post-operation verification failed: Target directory '{targetPath}' does not exist.");
                    }
                    if (!crossVolume && Directory.Exists(sourcePath)) // On same volume, source should NOT exist after move
                    {
                         throw new IOException($"Post-move verification failed: Source directory '{sourcePath}' still exists after move operation.");
                    }
                     _logger.LogInfo($"Item {itemId}: Move/copy successful and verified to '{targetPath}'.", "DownloadedItemProcessor");


                    // --- Step 6: Delete Backup on Success ---
                    if (backupCreated)
                    {
                        _logger.LogInfo($"Item {itemId}: Deleting successful backup '{backupPath}'", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Removing temporary backup...");
                        try
                        {
                            Directory.Delete(backupPath, true);
                            await Task.Delay(50, cancellationToken);
                             if (Directory.Exists(backupPath)) _logger.LogWarning($"Item {itemId}: Backup directory '{backupPath}' still exists after delete attempt.", "DownloadedItemProcessor");
                             else _logger.LogDebug($"Item {itemId}: Backup deleted successfully.", "DownloadedItemProcessor");
                        }
                        catch (Exception bkDelEx)
                        {
                            // Log as warning, don't fail the overall success, but notify user
                            _logger.LogWarning($"Item {itemId}: FAILED to delete backup '{backupPath}' after successful update. Error: {bkDelEx.Message}. Manual cleanup might be needed.", "DownloadedItemProcessor");
                            AddLogMessage($"Warning: Item {itemId} updated, but failed to remove backup '{Path.GetFileName(backupPath)}'.");
                        }
                        backupCreated = false; // Mark backup as handled (or attempted)
                    }

                    // If we reach here, processing was successful
                    _logger.LogInfo($"Item {itemId} ('{itemName}') processing SUCCEEDED.", "DownloadedItemProcessor");
                    AddLogMessage($"Item {itemId} ({itemName}) successfully updated.");
                    return true;

                }
                catch (Exception moveCopyEx) // Catch failures during move/copy/verify/backup-delete
                {
                    _logger.LogError($"Item {itemId}: FAILED during file transfer/verification/cleanup: {moveCopyEx.Message}", "DownloadedItemProcessor");
                    AddLogMessage($"Error: Item {itemId} ({itemName}) failed during file transfer: {moveCopyEx.Message}");

                    // --- Step 7: Attempt Rollback from Backup ---
                    if (backupCreated)
                    {
                        _logger.LogWarning($"Item {itemId}: Attempting rollback from '{backupPath}'...", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Rolling back to previous version...");
                        bool rollbackSuccess = await AttemptRollbackAsync(targetPath, backupPath, itemId, cancellationToken);
                        if (rollbackSuccess)
                        {
                             AddLogMessage($"Item {itemId}: Previous version restored from backup.");
                        }
                        // Error messages added within AttemptRollbackAsync
                    }
                    else if (moveOrCopyAttempted) // Failed during move/copy, but no backup was created (e.g., first install)
                    {
                        _logger.LogWarning($"Item {itemId}: Move/Copy failed, no backup exists. Cleaning up potentially incomplete target '{targetPath}'.", "DownloadedItemProcessor");
                        AddLogMessage($"Item {itemId}: Cleaning up failed installation attempt...");
                        try
                        {
                            if (Directory.Exists(targetPath))
                            {
                                Directory.Delete(targetPath, true);
                                await Task.Delay(50, cancellationToken);
                                 _logger.LogInfo($"Item {itemId}: Cleaned up incomplete target '{targetPath}'.", "DownloadedItemProcessor");
                            }
                        }
                        catch (Exception partialDelEx)
                        {
                            _logger.LogError($"Item {itemId}: Failed to delete partial target '{targetPath}' during cleanup (no backup). Error: {partialDelEx.Message}", "DownloadedItemProcessor");
                            AddLogMessage($"Error: Item {itemId} failed post-download cleanup of '{Path.GetFileName(targetPath)}'. Manual check needed.");
                        }
                    }
                    // Ensure failure is returned
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Item {itemId}: Processing cancelled.", "DownloadedItemProcessor");
                AddLogMessage($"Item {itemId}: Processing cancelled.");
                 // Attempt rollback if a backup was made before cancellation
                 if (backupCreated) {
                     _logger.LogWarning($"Item {itemId}: Cancellation occurred after backup. Attempting rollback from '{backupPath}'.", "DownloadedItemProcessor");
                     await AttemptRollbackAsync(targetPath, backupPath, itemId, CancellationToken.None); // Use None token for critical cleanup
                 }
                throw; // Re-throw cancellation
            }
            catch (Exception ex) // Catch failures from Timestamp creation or Initial Backup attempt
            {
                _logger.LogError($"Item {itemId}: FAILED during initial processing steps (Timestamps or Backup Creation): {ex.Message}", "DownloadedItemProcessor");
                AddLogMessage($"Error processing item {itemId} ({itemName}) before file transfer: {ex.Message}.");
                // No rollback needed here usually, as the target wasn't touched yet, or backup failed.
                return false;
            }
        }

         /// <summary>
        /// Attempts to restore the original mod directory from the backup.
        /// </summary>
        private async Task<bool> AttemptRollbackAsync(string targetPath, string backupPath, string itemId, CancellationToken cancellationToken)
        {
             // 1. Clean potentially incomplete/failed target first
             if (Directory.Exists(targetPath))
             {
                 _logger.LogDebug($"Item {itemId}: Rollback - Deleting incomplete target '{targetPath}' before restoring backup.", "DownloadedItemProcessor");
                 try
                 {
                     Directory.Delete(targetPath, true);
                     await Task.Delay(50, cancellationToken);
                     if (Directory.Exists(targetPath)) throw new IOException("Target directory still exists after delete attempt during rollback.");
                     _logger.LogDebug($"Item {itemId}: Rollback - Incomplete target deleted.", "DownloadedItemProcessor");
                 }
                 catch (Exception targetDelEx)
                 {
                     _logger.LogError($"Item {itemId}: CRITICAL ROLLBACK FAILURE - Failed to delete incomplete target '{targetPath}'. Error: {targetDelEx.Message}", "DownloadedItemProcessor");
                     AddLogMessage($"CRITICAL ERROR: Item {itemId} rollback failed! Cannot delete broken new version at '{Path.GetFileName(targetPath)}'. Backup remains at '{Path.GetFileName(backupPath)}'. Manual intervention required.");
                     return false; // Rollback failed critically
                 }
             }

             // 2. Rename backup back to target
             try
             {
                  _logger.LogDebug($"Item {itemId}: Rollback - Renaming backup '{backupPath}' back to '{targetPath}'.", "DownloadedItemProcessor");
                 Directory.Move(backupPath, targetPath);
                 await Task.Delay(50, cancellationToken);
                 if (!Directory.Exists(targetPath) || Directory.Exists(backupPath)) throw new IOException("Rollback rename verification failed.");
                 _logger.LogInfo($"Item {itemId}: Rollback successful. Restored '{targetPath}' from backup.", "DownloadedItemProcessor");
                 return true; // Rollback succeeded
             }
             catch (Exception restoreEx)
             {
                 _logger.LogError($"Item {itemId}: CRITICAL ROLLBACK FAILURE - Failed to restore backup '{backupPath}' to '{targetPath}'. Error: {restoreEx.Message}", "DownloadedItemProcessor");
                 AddLogMessage($"CRITICAL ERROR: Item {itemId} rollback failed! Could not restore backup '{Path.GetFileName(backupPath)}'. Backup remains. Manual intervention required.");
                 return false; // Rollback failed critically
             }
        }


        /// <summary>
        /// Recursively copies a directory and its contents. Handles cancellation.
        /// (Copied helper method - could be moved to a shared utility if used elsewhere)
        /// </summary>
        private async Task CopyDirectoryRecursivelyAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
            }

            // Ensure the destination directory exists.
            Directory.CreateDirectory(destinationDir);
            cancellationToken.ThrowIfCancellationRequested();

            // Copy files
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, file.Name);
                try
                {
                    file.CopyTo(tempPath, true); // Overwrite existing files
                }
                catch(IOException ioEx) when ((ioEx.HResult & 0xFFFF) == 32) // ERROR_SHARING_VIOLATION
                {
                     _logger.LogWarning($"Sharing violation copying '{file.Name}' to '{destinationDir}'. Retrying once...", "DownloadedItemProcessor");
                     await Task.Delay(250, cancellationToken); // Wait a bit
                     file.CopyTo(tempPath, true); // Retry once
                }
                await Task.Yield(); // Be responsive during potentially long copies
            }

            // Recursively copy subdirectories
            DirectoryInfo[] dirs = dir.GetDirectories();
            foreach (DirectoryInfo subdir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempPath = Path.Combine(destinationDir, subdir.Name);
                await CopyDirectoryRecursivelyAsync(subdir.FullName, tempPath, cancellationToken);
            }
        }

        // --- Logging Message Handling ---
        public void AddLogMessage(string message)
        {
            _logMessages.Add(message);
        }

        public List<string> GetLogMessages()
        {
            return new List<string>(_logMessages); // Return a copy
        }
        // --- End Logging Message Handling ---

    }
}