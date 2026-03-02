#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using System.Diagnostics;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class WorkshopUpdateCheckerService : IWorkshopUpdateCheckerService
    {
        private readonly ISteamApiClient _steamApiClient;
        private readonly IDownloadQueueService _downloadQueueService;
        private const string RimWorldAppId = "294100";
        private const string LocalDateFormat = "dd/MM/yyyy HH:mm:ss";
        private const int MaxParallelOperations = 10;

        public WorkshopUpdateCheckerService(ISteamApiClient steamApiClient, IDownloadQueueService downloadQueueService)
        {
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck)
        {
            return await CheckForUpdatesAsync(modsToCheck, null, default);
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck,
            IProgress<(int current, int total, string modName)>? progress,
            CancellationToken cancellationToken = default)
        {
            var result = new UpdateCheckResult();
            if (modsToCheck == null) return result;

            var validMods = modsToCheck
                .Where(mod => mod != null &&
                       !string.IsNullOrWhiteSpace(mod.SteamId) &&
                       long.TryParse(mod.SteamId, out _))
                .ToList();

            if (!validMods.Any()) return result;

            using var semaphore = new SemaphoreSlim(MaxParallelOperations);
            var tasks = new List<Task>();
            var errorMessages = new System.Collections.Concurrent.ConcurrentBag<string>();

            int totalMods = validMods.Count;
            int completedMods = 0;

            progress?.Report((0, totalMods, "Starting update check..."));

            foreach (var mod in validMods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int currentCount = Interlocked.Increment(ref completedMods);
                        progress?.Report((currentCount, totalMods, mod.Name));

                        await CheckModForUpdateAsync(mod, result, errorMessages, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[CheckModForUpdateAsync Task] Cancelled for mod {mod.Name} ({mod.SteamId}).");
                    }
                    catch (Exception ex)
                    {
                         result.IncrementErrorsEncountered();
                         errorMessages.Add($"Unexpected task error for '{mod.Name}' ({mod.SteamId}): {ex.Message}");
                         Debug.WriteLine($"[CheckModForUpdateAsync Task] Error for {mod.Name} ({mod.SteamId}): {ex}");
                    }
                    finally
                    {
                         semaphore.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                 await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                 Debug.WriteLine("[CheckForUpdatesAsync] Operation cancelled while waiting for tasks.");
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"[CheckForUpdatesAsync] Error during Task.WhenAll: {ex}");
            }

            result.ModsChecked = completedMods;
            result.ErrorMessages.AddRange(errorMessages);

            progress?.Report((totalMods, totalMods, "Update check finished."));

            return result;
        }

        private async Task CheckModForUpdateAsync(
            ModItem mod,
            UpdateCheckResult result,
            System.Collections.Concurrent.ConcurrentBag<string> errorMessages, 
            CancellationToken cancellationToken)
        {
            SteamApiResponse? apiResponse = null; 
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                apiResponse = await _steamApiClient.GetFileDetailsAsync(mod.SteamId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CheckModForUpdateAsync] API call cancelled for {mod.Name} ({mod.SteamId}).");
                throw;
            }
            catch (Exception ex)
            {
                result.IncrementErrorsEncountered();
                string errorDescription = SteamApiResultHelper.GetDescription((int)SteamApiResultCode.FileNotFound);
                errorMessages.Add($"API Error for '{mod.Name}' ({mod.SteamId}): {errorDescription} (Details not returned in successful response)");
                Debug.WriteLine($"Error during API call for {mod.SteamId}: {ex}");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
            {
                result.IncrementErrorsEncountered();
                errorMessages.Add($"No details returned for '{mod.Name}' ({mod.SteamId}). Mod might be removed or hidden.");
                return;
            }

            var details = apiResponse.Response.PublishedFileDetails.First();

            if (details.Result != (int)SteamApiResultCode.OK)
            {
                result.IncrementErrorsEncountered();
                string errorDescription = SteamApiResultHelper.GetDescription(details.Result);
                errorMessages.Add($"Mod '{mod.Name}' ({mod.SteamId}) - Code {details.Result}: {errorDescription}");
                return;
            }

            if (details.ConsumerAppId.ToString() != RimWorldAppId)
            {
                 result.IncrementErrorsEncountered();
                 errorMessages.Add($"Mod '{details.Title}' ({mod.SteamId}) is not a RimWorld ({RimWorldAppId}) mod (AppID: {details.ConsumerAppId}). Skipping.");
                 return;
            }

            if (string.IsNullOrWhiteSpace(mod.UpdateDate))
            {
                Debug.WriteLine($"Skipping update check for '{mod.Name}' ({mod.SteamId}) due to missing local UpdateDate.");
                return;
            }

            if (!TryParseLocalDate(mod.UpdateDate, out DateTime localUpdateTimeUtc))
            {
                result.IncrementErrorsEncountered();
                errorMessages.Add($"Could not parse local date '{mod.UpdateDate}' for mod '{mod.Name}' ({mod.SteamId}). Format expected: '{LocalDateFormat}'.");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
            DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;

            TimeSpan difference = apiUpdateTimeUtc - localUpdateTimeUtc;

            bool isConsideredUpdate = apiUpdateTimeUtc > localUpdateTimeUtc && difference.TotalMinutes > 2;

            if (isConsideredUpdate)
            {
                Debug.WriteLine($"Update found for '{details.Title}' ({mod.SteamId}). API UTC: {apiUpdateTimeUtc:O}, Local UTC: {localUpdateTimeUtc:O}");
                result.IncrementUpdatesFound();

                var modInfo = new ModInfoDto
                {
                    Name = details.Title ?? mod.Name,
                    SteamId = mod.SteamId,
                    Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={mod.SteamId}",
                    PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                    StandardDate = apiUpdateTimeUtc.ToString(LocalDateFormat, CultureInfo.InvariantCulture),
                    FileSize = details.FileSize,
                    LatestVersions = SteamApiResultHelper.ExtractAndSortVersionTags(details.Tags)
                };

                try
                {
                     _downloadQueueService.AddToQueue(modInfo);
                }
                catch (Exception ex)
                {
                     result.IncrementErrorsEncountered();
                     errorMessages.Add($"Failed to add updated mod '{mod.Name}' ({mod.SteamId}) to queue: {ex.Message}");
                     Debug.WriteLine($"Error adding mod {mod.SteamId} to queue: {ex}");
                }
            }
            else
            {
                if (apiUpdateTimeUtc > localUpdateTimeUtc)
                    Debug.WriteLine($"Mod '{details.Title}' ({mod.SteamId}) has newer timestamp but likely a timezone artifact. API UTC: {apiUpdateTimeUtc:O}, Local UTC: {localUpdateTimeUtc:O}");
                else
                    Debug.WriteLine($"Mod '{details.Title}' ({mod.SteamId}) is up-to-date. API UTC: {apiUpdateTimeUtc:O}, Local UTC: {localUpdateTimeUtc:O}");
            }
        }

        private bool TryParseLocalDate(string dateString, out DateTime utcDateTime)
        {
            utcDateTime = default;
            try
            {
                if (DateTime.TryParseExact(dateString, LocalDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utcDateTime))
                {
                    return true;
                }
                else
                {
                    Debug.WriteLine($"Failed to parse date string '{dateString}' with format '{LocalDateFormat}'.");
                    return false;
                }
            }
            catch (FormatException ex)
            {
                Debug.WriteLine($"Format exception parsing date '{dateString}': {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error parsing date '{dateString}': {ex.Message}");
                return false;
            }
        }
    }
}
