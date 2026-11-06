using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Service for checking and applying workshop mod updates.
    /// Based on RimSort's query_workshop_update_data approach.
    /// </summary>
    public class WorkshopUpdateService : IWorkshopUpdateService
    {
        private readonly GameInstance _gameInstance;
        private readonly ILoggerService _logger;
        private readonly SteamWorkshopApiClient _apiClient;

        public WorkshopUpdateService(GameInstance gameInstance, ILoggerService logger, SteamWorkshopApiClient apiClient)
        {
            _gameInstance = gameInstance ?? throw new ArgumentNullException(nameof(gameInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// Check for updates for workshop mods.
        /// </summary>
        public async Task<WorkshopUpdateResult> CheckForUpdatesAsync(IEnumerable<ModItem> mods, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new WorkshopUpdateResult();
            
            // Filter workshop mods
            var workshopMods = mods.Where(m => !string.IsNullOrEmpty(m.SteamId) && 
                                            long.TryParse(m.SteamId, out _) && 
                                            (m.ModType == ModType.Workshop || m.ModType == ModType.Tracked)).ToList();

            if (!workshopMods.Any())
            {
                result.Status = WorkshopUpdateStatus.NoWorkshopMods;
                result.ModsChecked = 0;
                return result;
            }

            result.ModsChecked = workshopMods.Count;
            progress?.Report((0, result.ModsChecked, "Starting update check..."));

            // Get mod details from Steam API
            var steamIds = workshopMods.Select(m => m.SteamId!).ToList();
            var modDetails = await _apiClient.GetModDetailsBatchAsync(steamIds, cancellationToken);

            foreach (var mod in workshopMods)
            {
                try
                {
                    var modDetail = modDetails.FirstOrDefault(d => d.PublishedFileId == mod.SteamId);
                    if (modDetail != null)
                    {
                        var hasUpdate = await CheckModForUpdateAsync(mod, modDetail, cancellationToken);
                        
                        if (hasUpdate)
                        {
                            result.UpdatesAvailable++;
                            result.UpdatesApplied++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.FailedModIds.Add(mod.SteamId!);
                    result.ErrorMessages.Add($"Error checking mod {mod.DisplayName}: {ex.Message}");
                    _logger.LogError($"Error checking mod {mod.DisplayName}: {ex.Message}", nameof(WorkshopUpdateService));
                }
            }

            // Determine status
            if (result.FailedModIds.Any())
            {
                result.Status = result.UpdatesAvailable > 0 ? WorkshopUpdateStatus.Partial : WorkshopUpdateStatus.Failed;
            }
            else
            {
                result.Status = result.UpdatesAvailable > 0 ? WorkshopUpdateStatus.Success : WorkshopUpdateStatus.Success;
            }

            return result;
        }

        /// <summary>
        /// Get update information for a specific mod.
        /// </summary>
        public async Task<ModUpdateInfo?> GetUpdateInfoAsync(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                return null;
            }

            try
            {
                var modDetail = await _apiClient.GetModDetailsAsync(steamId);
                if (modDetail == null)
                {
                    return null;
                }

                var updateInfo = new ModUpdateInfo
                {
                    SteamId = steamId,
                    LatestVersion = modDetail.Title ?? "Unknown",
                    LatestUpdate = modDetail.TimeUpdated != null ? 
                        DateTimeOffset.FromUnixTimeSeconds(modDetail.TimeUpdated.Value).DateTime : null,
                    FileSize = modDetail.FileSize
                };

                // Check if we have local info for comparison
                var localMod = FindLocalModBySteamId(steamId);
                if (localMod != null)
                {
                    updateInfo.CurrentVersion = localMod.DisplayName;
                    updateInfo.LastUpdated = localMod.LastUpdated;
                    updateInfo.UpdateAvailable = modDetail.TimeUpdated != null && 
                                               localMod.LastUpdated < DateTimeOffset.FromUnixTimeSeconds(modDetail.TimeUpdated.Value).DateTime;
                }

                return updateInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting update info for mod {steamId}: {ex.Message}", nameof(WorkshopUpdateService));
                return null;
            }
        }

        /// <summary>
        /// Update a specific workshop mod.
        /// </summary>
        public async Task<bool> UpdateModAsync(string steamId, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !_gameInstance.IsSteamInstallation)
            {
                return false;
            }

            try
            {
                progress?.Report((1, 3, $"Updating mod {steamId}..."));

                // Use steam:// protocol to trigger Steam update
                var steamUrl = $"steam://rungameid/294100//update/{steamId}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = steamUrl,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    // Wait a bit for Steam to start processing
                    await Task.Delay(5000, cancellationToken);
                }

                progress?.Report((2, 3, "Waiting for Steam to download update..."));

                // Wait for the update to complete
                var updated = await WaitForModUpdateAsync(steamId, cancellationToken);
                
                if (updated)
                {
                    progress?.Report((3, 3, "Mod updated successfully"));
                    _logger.LogInfo($"Successfully updated mod {steamId}", nameof(WorkshopUpdateService));
                    return true;
                }
                else
                {
                    _logger.LogError($"Failed to update mod {steamId} - update timed out", nameof(WorkshopUpdateService));
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating mod {steamId}: {ex.Message}", nameof(WorkshopUpdateService));
                return false;
            }
        }

        /// <summary>
        /// Update multiple workshop mods.
        /// </summary>
        public async Task<WorkshopUpdateBatchResult> UpdateModsAsync(IEnumerable<string> steamIds, IProgress<(int current, int total, string message)>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new WorkshopUpdateBatchResult
            {
                TotalMods = steamIds.Count()
            };

            var steamIdList = steamIds.ToList();
            for (int i = 0; i < steamIdList.Count; i++)
            {
                var steamId = steamIdList[i];
                
                try
                {
                    progress?.Report((i + 1, result.TotalMods, $"Updating mod {steamId}..."));
                    
                    var success = await UpdateModAsync(steamId, progress, cancellationToken);
                    if (success)
                    {
                        result.SuccessfullyUpdated++;
                        result.SuccessfullyUpdatedIds.Add(steamId);
                    }
                    else
                    {
                        result.FailedToUpdate++;
                        result.FailedToUpdateIds.Add(steamId);
                    }
                }
                catch (Exception ex)
                {
                    result.FailedToUpdate++;
                    result.FailedToUpdateIds.Add(steamId);
                    result.ErrorMessages.Add($"Error updating mod {steamId}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a specific mod has updates available.
        /// </summary>
        private async Task<bool> CheckModForUpdateAsync(ModItem mod, WorkshopModDetails modDetail, CancellationToken cancellationToken)
        {
            if (modDetail.TimeUpdated == null)
            {
                return false;
            }

            var lastUpdateTime = DateTimeOffset.FromUnixTimeSeconds(modDetail.TimeUpdated.Value);
            
            // Compare with mod's last updated time
            if (mod.LastUpdated.HasValue && mod.LastUpdated.Value >= lastUpdateTime)
            {
                return false;
            }

            // If no local update time, assume it needs updating
            return true;
        }

        /// <summary>
        /// Find a local mod by Steam ID.
        /// </summary>
        private ModItem? FindLocalModBySteamId(string steamId)
        {
            // This would need to be implemented based on your ModService
            // For now, return null as a placeholder
            return null;
        }

        /// <summary>
        /// Wait for a mod to be updated.
        /// </summary>
        private async Task<bool> WaitForModUpdateAsync(string steamId, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromMinutes(15); // 15 minute timeout
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(_gameInstance.WorkshopFolder))
                {
                    return false;
                }

                // Check if mod was updated by comparing file times
                var modPath = Path.Combine(_gameInstance.WorkshopFolder, steamId);
                if (Directory.Exists(modPath))
                {
                    var files = Directory.GetFiles(modPath, "*", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        var newestFile = files
                            .Select(f => new FileInfo(f).LastWriteTimeUtc)
                            .Max();
                        
                        if (DateTime.UtcNow - newestFile < TimeSpan.FromMinutes(1)) // Recently updated
                        {
                            return true;
                        }
                    }
                }

                await Task.Delay(2000, cancellationToken); // Check every 2 seconds
            }

            return false;
        }
    }
}