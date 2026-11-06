using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Services;
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
        private const long RimWorldAppId = 294100;
        private const string LocalDateFormat = "dd/MM/yyyy HH:mm:ss";
        private const long ClockSkewToleranceSeconds = 120;

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

            // Workshop-identity mods only: Steam client copies + app-downloaded (Tracked) copies
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

            // Get mod details from Steam API (batched client-side, 300 per request)
            var steamIds = workshopMods.Select(m => m.SteamId!).ToList();
            var modDetails = await _apiClient.GetModDetailsBatchAsync(steamIds, cancellationToken);

            var detailsById = modDetails
                .Where(d => !string.IsNullOrEmpty(d.PublishedFileId))
                .ToDictionary(d => d.PublishedFileId!, d => d, StringComparer.Ordinal);

            // Progress contract: (modsCheckedSoFar, totalMods, current mod's display name).
            int checkedCount = 0;
            foreach (var mod in workshopMods)
            {
                checkedCount++;
                progress?.Report((checkedCount, result.ModsChecked, mod.DisplayName));

                try
                {
                    if (!detailsById.TryGetValue(mod.SteamId!, out var modDetail))
                    {
                        result.FailedModIds.Add(mod.SteamId!);
                        result.ErrorMessages.Add($"No details returned for '{mod.DisplayName}' ({mod.SteamId}). Mod might be removed or hidden.");
                        _logger.LogWarning($"No API details returned for mod {mod.SteamId}.", nameof(WorkshopUpdateService));
                        continue;
                    }

                    if (modDetail.Result != 1) // k_EResultOK
                    {
                        result.FailedModIds.Add(mod.SteamId!);
                        result.ErrorMessages.Add($"Mod '{mod.DisplayName}' ({mod.SteamId}) - Code {modDetail.Result}: {SteamApiResultHelper.GetDescription(modDetail.Result)}");
                        _logger.LogWarning($"API result {modDetail.Result} for mod {mod.SteamId}.", nameof(WorkshopUpdateService));
                        continue;
                    }

                    if (modDetail.ConsumerAppId.HasValue && modDetail.ConsumerAppId.Value != RimWorldAppId)
                    {
                        result.FailedModIds.Add(mod.SteamId!);
                        result.ErrorMessages.Add($"Mod '{modDetail.Title}' ({mod.SteamId}) is not a RimWorld ({RimWorldAppId}) mod (AppID: {modDetail.ConsumerAppId.Value}). Skipping.");
                        _logger.LogWarning($"Mod {mod.SteamId} is for AppId {modDetail.ConsumerAppId.Value}, not RimWorld.", nameof(WorkshopUpdateService));
                        continue;
                    }

                    // Internal = max(ACF manifest time, timestamp.txt-derived UpdateDate);
                    // ACF is populated during scan by the ACF timestamp service.
                    var internalTime = Math.Max(mod.AcfTimeUpdated ?? 0, GetTimestampUnix(mod.UpdateDate));
                    var externalTime = modDetail.TimeUpdated ?? 0;

                    var decision = WorkshopUpdateDecider.Decide(externalTime, internalTime);

                    // Keep the historical "clock-skew tolerance" behavior: a remote stamp
                    // barely newer than the local one is treated as UpToDate, because
                    // timestamp.txt is written with second granularity by whatever tool
                    // last touched it and tiny deltas are almost never real revisions.
                    if (decision == WorkshopUpdateDecision.UpdateAvailable &&
                        externalTime - internalTime <= ClockSkewToleranceSeconds)
                    {
                        decision = WorkshopUpdateDecision.UpToDate;
                    }

                    result.Updates.Add(new WorkshopUpdateCandidate
                    {
                        Mod = mod,
                        Details = modDetail,
                        ExternalTimeUpdated = externalTime,
                        InternalTime = internalTime,
                        Decision = decision
                    });

                    if (decision == WorkshopUpdateDecision.UpdateAvailable)
                    {
                        result.UpdatesAvailable++;
                        _logger.LogInfo(
                            $"Update available for '{mod.DisplayName}' ({mod.SteamId}): external {externalTime} > internal {internalTime}.",
                            nameof(WorkshopUpdateService));
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
                result.Status = WorkshopUpdateStatus.Success;
            }

            return result;
        }

        /// <summary>
        /// Parse the timestamp.txt-derived UpdateDate ("dd/MM/yyyy HH:mm:ss", treated as
        /// UTC — the same contract RimSharp's own downloader historically wrote) into
        /// unix seconds. 0 when absent/unparseable. This is the single implementation of
        /// that conversion; the per-mod date-parsing dance in the downloader checker was
        /// deleted in favor of this one.
        /// </summary>
        private static long GetTimestampUnix(string? updateDate)
        {
            if (string.IsNullOrWhiteSpace(updateDate))
            {
                return 0;
            }

            if (DateTime.TryParseExact(
                    updateDate,
                    LocalDateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return new DateTimeOffset(parsed, TimeSpan.Zero).ToUnixTimeSeconds();
            }

            return 0;
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