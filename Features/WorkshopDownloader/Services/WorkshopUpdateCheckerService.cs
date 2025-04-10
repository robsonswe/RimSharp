#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models; // For ModItem
using System.Diagnostics; // For Debug.WriteLine

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
        private const int MaxParallelOperations = 10; // Reduced slightly for API calls

        public WorkshopUpdateCheckerService(ISteamApiClient steamApiClient, IDownloadQueueService downloadQueueService)
        {
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
        }

        // Overload to maintain compatibility if called without progress/token
        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck)
        {
            return await CheckForUpdatesAsync(modsToCheck, null, default);
        }

        // Main implementation now accepting CancellationToken
        public async Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck,
            IProgress<(int current, int total, string modName)>? progress, // Nullable progress
            CancellationToken cancellationToken = default) // <--- ADDED Parameter
        {
            var result = new UpdateCheckResult();
            if (modsToCheck == null) return result;

            // Filter out mods without valid Steam IDs first
            var validMods = modsToCheck
                .Where(mod => mod != null &&
                       !string.IsNullOrWhiteSpace(mod.SteamId) &&
                       long.TryParse(mod.SteamId, out _))
                .ToList();

            if (!validMods.Any()) return result; // Nothing to check

            // Use SemaphoreSlim to limit concurrent API calls
            using var semaphore = new SemaphoreSlim(MaxParallelOperations);

            var tasks = new List<Task>();
            // Use ConcurrentBag for thread-safe error message collection
            var errorMessages = new System.Collections.Concurrent.ConcurrentBag<string>();

            int totalMods = validMods.Count;
            int completedMods = 0;

            progress?.Report((0, totalMods, "Starting update check...")); // Initial progress report

            foreach (var mod in validMods)
            {
                // Check for cancellation before starting work on a new mod
                cancellationToken.ThrowIfCancellationRequested();

                // Wait asynchronously for the semaphore, respecting cancellation
                await semaphore.WaitAsync(cancellationToken);

                // Add the task processing this mod
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Check cancellation again inside the task
                        cancellationToken.ThrowIfCancellationRequested();

                        int currentCount = Interlocked.Increment(ref completedMods); // Increment first for reporting
                        progress?.Report((currentCount, totalMods, mod.Name));

                        // Pass cancellation token down
                        await CheckModForUpdateAsync(mod, result, errorMessages, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Don't increment errors if cancelled, just stop processing this mod
                        Debug.WriteLine($"[CheckModForUpdateAsync Task] Cancelled for mod {mod.Name} ({mod.SteamId}).");
                        // Optional: Add a specific cancellation message if needed, but often just stopping is enough
                    }
                    catch (Exception ex)
                    {
                         // Catch unexpected errors within the task execution itself
                         result.IncrementErrorsEncountered();
                         errorMessages.Add($"Unexpected task error for '{mod.Name}' ({mod.SteamId}): {ex.Message}");
                         Debug.WriteLine($"[CheckModForUpdateAsync Task] Error for {mod.Name} ({mod.SteamId}): {ex}");
                    }
                    finally
                    {
                         semaphore.Release();
                         // Report final progress for this item if needed (already reported when starting)
                         // int finalCount = completedMods; // Read the potentially updated value
                         // progress?.Report((finalCount, totalMods, $"{mod.Name} check finished"));
                    }
                }, cancellationToken)); // Pass token to Task.Run to prevent starting if already cancelled
            }

            try
            {
                 // Wait for all tasks to complete or be cancelled/fault
                 await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                 // This catches cancellation that might occur during Task.WhenAll itself
                 Debug.WriteLine("[CheckForUpdatesAsync] Operation cancelled while waiting for tasks.");
                 // Tasks themselves handle internal cancellation checks
            }
            catch (Exception ex)
            {
                 // Catch potential aggregation errors from Task.WhenAll, though individual task errors are handled above
                 Debug.WriteLine($"[CheckForUpdatesAsync] Error during Task.WhenAll: {ex}");
                 // Report a general error? Individual errors are already captured.
                 // result.IncrementErrorsEncountered(); // Maybe not needed if task errors are caught
                 // errorMessages.Add($"General error waiting for update tasks: {ex.Message}");
            }


            // Populate results after all tasks are processed
            result.ModsChecked = completedMods; // Number of mods actually processed (started)
            // UpdatesFound and ErrorsEncountered are incremented atomically within CheckModForUpdateAsync

            // Add collected error messages
            result.ErrorMessages.AddRange(errorMessages);

            progress?.Report((totalMods, totalMods, "Update check finished.")); // Final progress report

            return result;
        }

        private async Task CheckModForUpdateAsync(
            ModItem mod,
            UpdateCheckResult result,
            System.Collections.Concurrent.ConcurrentBag<string> errorMessages, // Use ConcurrentBag
            CancellationToken cancellationToken) // <--- ADDED Parameter
        {
            SteamApiResponse? apiResponse = null; // Nullable
            try
            {
                 // Check cancellation before making the API call
                cancellationToken.ThrowIfCancellationRequested();

                apiResponse = await _steamApiClient.GetFileDetailsAsync(mod.SteamId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[CheckModForUpdateAsync] API call cancelled for {mod.Name} ({mod.SteamId}).");
                throw; // Re-throw cancellation to be caught by the Task.Run catch block
            }
            catch (Exception ex)
            {
                result.IncrementErrorsEncountered();
                errorMessages.Add($"Failed API call for '{mod.Name}' ({mod.SteamId}): {ex.Message}");
                Debug.WriteLine($"Error during API call for {mod.SteamId}: {ex}");
                return; // Exit processing for this mod on API error
            }

            // Check cancellation after the API call returns
            cancellationToken.ThrowIfCancellationRequested();

            if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
            {
                result.IncrementErrorsEncountered();
                errorMessages.Add($"No details returned for '{mod.Name}' ({mod.SteamId}). Mod might be removed or hidden.");
                return;
            }

            var details = apiResponse.Response.PublishedFileDetails.First();

            if (details.Result != 1)
            {
                result.IncrementErrorsEncountered();
                errorMessages.Add($"API indicated failure retrieving details for '{mod.Name}' ({mod.SteamId}). Result Code: {details.Result}");
                return;
            }

            // Optional: Check AppID (already present, seems fine)
            if (details.ConsumerAppId.ToString() != RimWorldAppId)
            {
                 result.IncrementErrorsEncountered();
                 errorMessages.Add($"Mod '{details.Title}' ({mod.SteamId}) is not a RimWorld ({RimWorldAppId}) mod (AppID: {details.ConsumerAppId}). Skipping.");
                 return;
            }


            if (string.IsNullOrWhiteSpace(mod.UpdateDate))
            {
                // This is not necessarily an error, just can't compare
                Debug.WriteLine($"Skipping update check for '{mod.Name}' ({mod.SteamId}) due to missing local UpdateDate.");
                return;
            }

            if (!TryParseLocalDate(mod.UpdateDate, out DateTime localUpdateTimeUtc))
            {
                result.IncrementErrorsEncountered();
                errorMessages.Add($"Could not parse local date '{mod.UpdateDate}' for mod '{mod.Name}' ({mod.SteamId}). Format expected: '{LocalDateFormat}'.");
                return;
            }

            // Check cancellation before comparison and adding to queue
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
            DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;
            
            // --- BEGIN TIMEZONE ARTIFACT DETECTION LOGIC ---
            // Check if the difference might be just a timezone artifact
            TimeSpan rawDifference = apiUpdateTimeUtc - localUpdateTimeUtc;
            
            // Extract only time parts (hours, minutes) for checking timezone related patterns
            DateTime apiTimeOnly = new DateTime(1, 1, 1, apiUpdateTimeUtc.Hour, apiUpdateTimeUtc.Minute, 0);
            DateTime localTimeOnly = new DateTime(1, 1, 1, localUpdateTimeUtc.Hour, localUpdateTimeUtc.Minute, 0);

            // Calculate minute difference between the times (irrespective of date)
            int minutesDifference = (int)Math.Abs((apiTimeOnly - localTimeOnly).TotalMinutes);
            
            // Handle wrap-around difference (e.g., 23:30 vs 00:30 = 1 hour difference, not 23 hours)
            if (minutesDifference > 12 * 60)
            {
                minutesDifference = 24 * 60 - minutesDifference;
            }

            // Check for common timezone offset patterns (whole hours, half-hours, quarter-hours)
            bool isLikelyTimezoneArtifact = false;
            if (minutesDifference % 60 == 0)         // Full hour offsets (most timezone differences)
            {
                isLikelyTimezoneArtifact = true;
            }
            else if (minutesDifference % 30 == 0)    // Half-hour offsets (like India, Australia)
            {
                isLikelyTimezoneArtifact = true;
            }
            else if (minutesDifference % 15 == 0)    // Quarter-hour offsets (like Nepal, Chatham Islands)
            {
                isLikelyTimezoneArtifact = true;
            }
            
            // Log the detailed timezone detection info for troubleshooting
            Debug.WriteLine($"Timezone detection for '{mod.Name}' ({mod.SteamId}): Minutes difference: {minutesDifference}, Likely TZ artifact: {isLikelyTimezoneArtifact}");
            // --- END TIMEZONE ARTIFACT DETECTION LOGIC ---

            // Only consider it an update if the API time is newer AND it's not just a timezone artifact
            if (apiUpdateTimeUtc > localUpdateTimeUtc && !isLikelyTimezoneArtifact)
            {
                Debug.WriteLine($"Update found for '{details.Title}' ({mod.SteamId}). API UTC: {apiUpdateTimeUtc:O}, Local UTC: {localUpdateTimeUtc:O}");
                result.IncrementUpdatesFound();

                var modInfo = new ModInfoDto
                {
                    Name = details.Title ?? mod.Name,
                    SteamId = mod.SteamId,
                    Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={mod.SteamId}",
                    PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture), // Use API Offset for original TZ if needed, or UTC
                    StandardDate = apiUpdateTimeUtc.ToString(LocalDateFormat, CultureInfo.InvariantCulture) // Store consistent format (UTC recommended)
                };

                try
                {
                     // Add to queue (Assuming AddToQueue is thread-safe or handled appropriately by the service)
                     // This operation should ideally be quick. If not, consider making it async and cancellable too.
                     _downloadQueueService.AddToQueue(modInfo);
                }
                catch (Exception ex)
                {
                     // Handle potential errors adding to the queue
                     result.IncrementErrorsEncountered();
                     errorMessages.Add($"Failed to add updated mod '{mod.Name}' ({mod.SteamId}) to queue: {ex.Message}");
                     Debug.WriteLine($"Error adding mod {mod.SteamId} to queue: {ex}");
                     // Decrement updates found since it wasn't successfully queued? Optional, depends on desired reporting.
                     // result.DecrementUpdatesFound(); // Need to implement DecrementUpdatesFound if needed
                }
            }
            else
            {
                // Log more detailed info about why the update was skipped (either up-to-date or timezone artifact)
                if (apiUpdateTimeUtc > localUpdateTimeUtc)
                    Debug.WriteLine($"Mod '{details.Title}' ({mod.SteamId}) has newer timestamp but likely a timezone artifact. API UTC: {apiUpdateTimeUtc:O}, Local UTC: {localUpdateTimeUtc:O}");
                else
                    Debug.WriteLine($"Mod '{details.Title}' ({mod.SteamId}) is up-to-date. API UTC: {apiUpdateTimeUtc:O}, Local UTC: {localUpdateTimeUtc:O}");
            }
        }

        // (TryParseLocalDate method remains the same)
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
                    Debug.WriteLine($"Failed to parse date string '{dateString}' with format '{LocalDateFormat}'.");
                    return false;
                }
            }
            catch (FormatException ex)
            {
                Debug.WriteLine($"Format exception parsing date '{dateString}': {ex.Message}");
                return false;
            }
            catch (Exception ex) // Catch other potential exceptions during parsing/conversion
            {
                Debug.WriteLine($"Unexpected error parsing date '{dateString}': {ex.Message}");
                return false;
            }
        }
    }
}