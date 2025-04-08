using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models; // For ModItem

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class WorkshopUpdateCheckerService : IWorkshopUpdateCheckerService
    {
        private readonly ISteamApiClient _steamApiClient;
        private readonly IDownloadQueueService _downloadQueueService;
        private const string RimWorldAppId = "294100";
        // Define the exact format of your UpdateDate string
        private const string LocalDateFormat = "dd/MM/yyyy HH:mm:ss";
        // Maximum number of parallel operations
        private const int MaxParallelOperations = 10;

        public WorkshopUpdateCheckerService(ISteamApiClient steamApiClient, IDownloadQueueService downloadQueueService)
        {
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck)
        {
            return await CheckForUpdatesAsync(modsToCheck, null);
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck,
            IProgress<(int current, int total, string modName)> progress)
        {
            var result = new UpdateCheckResult();
            if (modsToCheck == null) return result;

            // Filter out mods without valid Steam IDs first
            var validMods = modsToCheck
                .Where(mod => mod != null &&
                       !string.IsNullOrWhiteSpace(mod.SteamId) &&
                       long.TryParse(mod.SteamId, out _))
                .ToList();

            // Use SemaphoreSlim to limit concurrent operations
            using var semaphore = new SemaphoreSlim(MaxParallelOperations);

            var tasks = new List<Task>();
            var errorMessages = new List<string>();

            int totalMods = validMods.Count;
            int completedMods = 0;

            foreach (var mod in validMods)
            {
                await semaphore.WaitAsync();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        progress?.Report((completedMods, totalMods, mod.Name));
                        await CheckModForUpdateAsync(mod, result, errorMessages);
                    }
                    finally
                    {
                        semaphore.Release();
                        Interlocked.Increment(ref completedMods);
                        progress?.Report((completedMods, totalMods, mod.Name));
                    }
                }));
            }

            await Task.WhenAll(tasks);

            result.ModsChecked = completedMods;
            result.ErrorsEncountered = errorMessages.Count;
            result.ErrorMessages.AddRange(errorMessages);

            return result;
        }

        private async Task CheckModForUpdateAsync(
            ModItem mod,
            UpdateCheckResult result,
            List<string> errorMessages)
        {
            SteamApiResponse apiResponse = null;
            try
            {
                apiResponse = await _steamApiClient.GetFileDetailsAsync(mod.SteamId);
            }
            catch (Exception ex)
            {
                result.IncrementErrorsEncountered();
                lock (errorMessages)
                {
                    errorMessages.Add($"Failed API call for '{mod.Name}' ({mod.SteamId}): {ex.Message}");
                }
                Console.WriteLine($"Error during API call for {mod.SteamId}: {ex}");
                return;
            }

            if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
            {
                result.IncrementErrorsEncountered();
                lock (errorMessages)
                {
                    errorMessages.Add($"No details returned for '{mod.Name}' ({mod.SteamId}). Mod might be removed or hidden.");
                }
                return;
            }

            var details = apiResponse.Response.PublishedFileDetails.First();

            if (details.Result != 1)
            {
                result.IncrementErrorsEncountered();
                lock (errorMessages)
                {
                    errorMessages.Add($"API indicated failure retrieving details for '{mod.Name}' ({mod.SteamId}).");
                }
                return;
            }

            if (details.ConsumerAppId.ToString() != RimWorldAppId)
            {
                result.IncrementErrorsEncountered();
                lock (errorMessages)
                {
                    errorMessages.Add($"Mod '{details.Title}' ({mod.SteamId}) is not a RimWorld ({RimWorldAppId}) mod (AppID: {details.ConsumerAppId}). Skipping.");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(mod.UpdateDate))
            {
                Console.WriteLine($"Skipping update check for '{mod.Name}' ({mod.SteamId}) due to missing local UpdateDate.");
                return;
            }

            if (!TryParseLocalDate(mod.UpdateDate, out DateTime localUpdateTimeUtc))
            {
                result.IncrementErrorsEncountered();
                lock (errorMessages)
                {
                    errorMessages.Add($"Could not parse local date '{mod.UpdateDate}' for mod '{mod.Name}' ({mod.SteamId}). Skipping update check.");
                }
                return;
            }

            DateTimeOffset apiUpdateTimeUtc = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
            var apiTime = apiUpdateTimeUtc.UtcDateTime;
            var localTime = localUpdateTimeUtc;

            TimeSpan rawDifference = apiTime - localTime;
            DateTime apiTimeOnly = new DateTime(1, 1, 1, apiTime.Hour, apiTime.Minute, 0);
            DateTime localTimeOnly = new DateTime(1, 1, 1, localTime.Hour, localTime.Minute, 0);

            int minutesDifference = (int)Math.Abs((apiTimeOnly - localTimeOnly).TotalMinutes);
            if (minutesDifference > 12 * 60)
            {
                minutesDifference = 24 * 60 - minutesDifference;
            }

            bool isLikelyTimezoneArtifact = false;
            if (minutesDifference % 60 == 0)
            {
                isLikelyTimezoneArtifact = true;
            }
            else if (minutesDifference % 60 == 30)
            {
                isLikelyTimezoneArtifact = true;
            }
            else if (minutesDifference % 60 == 45)
            {
                isLikelyTimezoneArtifact = true;
            }

            if (apiTime > localTime && !isLikelyTimezoneArtifact)
            {
                Console.WriteLine($"Update found for '{details.Title}' ({mod.SteamId}). API: {apiUpdateTimeUtc}, Local: {localUpdateTimeUtc}");
                result.IncrementUpdatesFound();

                var modInfo = new ModInfoDto
                {
                    Name = details.Title ?? mod.Name,
                    SteamId = mod.SteamId,
                    Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={mod.SteamId}",
                    PublishDate = apiUpdateTimeUtc.ToString("d MMM yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                    StandardDate = apiUpdateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                };

                await Task.Run(() =>
                {
                    _downloadQueueService.AddToQueue(modInfo);
                });
            }

        }
        private bool TryParseLocalDate(string dateString, out DateTime utcDateTime)
        {
            utcDateTime = default;
            try
            {
                // Parse the local date string using the specific format
                if (DateTime.TryParseExact(dateString, LocalDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime localTime))
                {
                    // Convert the parsed local time to UTC for comparison
                    utcDateTime = localTime.ToUniversalTime();
                    return true;
                }
                else
                {
                    Console.WriteLine($"Failed to parse date string '{dateString}' with format '{LocalDateFormat}'.");
                    return false;
                }
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"Format exception parsing date '{dateString}': {ex.Message}");
                return false;
            }
            catch (Exception ex) // Catch other potential exceptions during parsing/conversion
            {
                Console.WriteLine($"Unexpected error parsing date '{dateString}': {ex.Message}");
                return false;
            }
        }
    }
}