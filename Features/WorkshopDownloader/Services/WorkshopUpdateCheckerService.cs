#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Services.Workshop;
using RimSharp.Shared.Models;
using System.Diagnostics;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    /// <summary>
    /// Thin adapter between the workshop downloader UI/queue and the central
    /// WorkshopUpdateService (the single update checker). All timestamp
    /// comparison and decision logic lives in the central service; this class
    /// only translates results into downloader actions: enqueue a SteamCMD
    /// re-download for every updated mod the app owns (Type. Tracked).
    /// Steam-client-mods (Type.Workshop) updates are reported upstream but are
    /// never queued here — the Steam client owns those files, not this app.
    /// </summary>
    public class WorkshopUpdateCheckerService : IWorkshopUpdateCheckerService
    {
        private readonly IWorkshopUpdateService _updateService;
        private readonly IDownloadQueueService _downloadQueueService;
        private const string LocalDateFormat = "dd/MM/yyyy HH:mm:ss";

        public WorkshopUpdateCheckerService(IWorkshopUpdateService updateService, IDownloadQueueService downloadQueueService)
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck,
            IProgress<(int current, int total, string modName)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new UpdateCheckResult();
            if (modsToCheck == null) return result;

            var forwardProgress = progress != null
                ? new Progress<(int current, int total, string message)>(p =>
                    progress.Report((p.current, p.total, p.message)))
                : null;

            var central = await _updateService.CheckForUpdatesAsync(modsToCheck, forwardProgress, cancellationToken);

            result.ModsChecked = central.ModsChecked;
            result.ErrorMessages.AddRange(central.ErrorMessages);
            result.ErrorsEncountered = central.FailedModIds.Count;

            int queuedUpdates = 0;
            foreach (var candidate in central.Updates)
            {
                if (candidate.Decision != WorkshopUpdateDecision.UpdateAvailable)
                {
                    Debug.WriteLine(
                        $"[WorkshopUpdateCheckerService] '{candidate.Mod.DisplayName}' ({candidate.Mod.SteamId}) decision: {candidate.Decision}. Not queued.");
                    continue;
                }

                if (candidate.Mod.ModType != ModType.Tracked)
                {
                    // Steam client mods update through the Steam client — surface the
                    // decision (the caller can count/report it) but never re-download
                    // via SteamCMD what the client already owns.
                    Debug.WriteLine(
                        $"[WorkshopUpdateCheckerService] '{candidate.Mod.DisplayName}' ({candidate.Mod.SteamId}) is a Steam-client mod (Workshop type); update found but not queued.");
                    continue;
                }

                var modInfo = BuildModInfoDto(candidate);

                try
                {
                    _downloadQueueService.AddToQueue(modInfo);
                    queuedUpdates++;

                    Debug.WriteLine(
                        $"[WorkshopUpdateCheckerService] Update queued for '{candidate.Mod.DisplayName}' ({candidate.Mod.SteamId}).");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.ErrorsEncountered++;
                    result.ErrorMessages.Add($"Failed to add updated mod '{candidate.Mod.DisplayName}' ({candidate.Mod.SteamId}) to queue: {ex.Message}");
                    Debug.WriteLine($"[WorkshopUpdateCheckerService] Error adding mod {candidate.Mod.SteamId} to queue: {ex}");
                }
            }

            result.UpdatesFound = queuedUpdates;

            return result;
        }

        /// <summary>
        /// Maps a central-service update candidate into the downloader queue's
        /// ModInfoDto (title, URL, display-formatted dates, size, version tags).
        /// </summary>
        private static ModInfoDto BuildModInfoDto(WorkshopUpdateCandidate candidate)
        {
            var external = DateTimeOffset.FromUnixTimeSeconds(candidate.ExternalTimeUpdated);

            return new ModInfoDto
            {
                Name = candidate.Details.Title ?? candidate.Mod.DisplayName,
                SteamId = candidate.Mod.SteamId,
                Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={candidate.Mod.SteamId}",
                PublishDate = external.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                StandardDate = external.UtcDateTime.ToString(LocalDateFormat, CultureInfo.InvariantCulture),
                FileSize = candidate.Details.FileSize ?? 0,
                LatestVersions = SteamApiResultHelper.ExtractAndSortVersionTags(
                    candidate.Details.Tags?
                        .Select(t => new SteamTag { Tag = t.Tag ?? string.Empty })
                        .ToList())
            };
        }
    }
}
