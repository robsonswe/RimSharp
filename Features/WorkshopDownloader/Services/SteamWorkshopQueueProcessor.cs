#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class SteamWorkshopQueueProcessor : ISteamWorkshopQueueProcessor
    {
        private readonly ISteamApiClient _steamApiClient;
        private readonly IDownloadQueueService _downloadQueueService;
        private readonly ILoggerService _logger;
        private const int MaxParallelOperations = 10; // Or make configurable

        public SteamWorkshopQueueProcessor(
            ISteamApiClient steamApiClient,
            IDownloadQueueService downloadQueueService,
            ILoggerService logger)
        {
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _downloadQueueService = downloadQueueService ?? throw new ArgumentNullException(nameof(downloadQueueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<QueueProcessResult> ProcessAndEnqueueModsAsync(
            IEnumerable<string> steamIds,
            IProgress<QueueProcessProgress>? progress,
            CancellationToken cancellationToken)
        {
            var uniqueSteamIds = steamIds?.Distinct().ToList() ?? new List<string>();
            var result = new QueueProcessResult { TotalAttempted = uniqueSteamIds.Count };

            if (!uniqueSteamIds.Any())
            {
                _logger.LogDebug("ProcessAndEnqueueModsAsync called with empty list.", nameof(SteamWorkshopQueueProcessor));
                return result; // Nothing to process
            }

            _logger.LogInfo($"Starting Steam API check and queue process for {result.TotalAttempted} unique Steam IDs.", nameof(SteamWorkshopQueueProcessor));

            using var semaphore = new SemaphoreSlim(MaxParallelOperations);
            var tasks = new List<Task>();
            var errorMessagesBag = new ConcurrentBag<string>();
            var addedNamesBag = new ConcurrentBag<string>();

            // Thread-safe counters
            int processedCount = 0;
            int successfullyAddedCount = 0;
            int alreadyQueuedCount = 0;
            int failedProcessingCount = 0;

            try
            {
                foreach (var steamId in uniqueSteamIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await semaphore.WaitAsync(cancellationToken);

                    tasks.Add(Task.Run(async () =>
                    {
                        string currentSteamId = steamId; // Capture loop variable
                        string modIdentifier = $"Steam ID {currentSteamId}"; // Default identifier
                        string modNameForProgress = "Unknown"; // Default Name

                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int currentItemIndex = Interlocked.Increment(ref processedCount);

                            // --- Report Initial Progress ---
                            progress?.Report(new QueueProcessProgress
                            {
                                CurrentItem = currentItemIndex,
                                TotalItems = result.TotalAttempted,
                                CurrentSteamId = currentSteamId,
                                CurrentItemName = modNameForProgress,
                                Message = $"Checking {modIdentifier}..."
                            });

                            // --- Check if Already in Queue ---
                            if (_downloadQueueService.IsInQueue(currentSteamId))
                            {
                                Interlocked.Increment(ref alreadyQueuedCount);
                                _logger.LogDebug($"{modIdentifier} is already in the download queue.", nameof(SteamWorkshopQueueProcessor));
                                progress?.Report(new QueueProcessProgress { /*...*/ Message = $"{modIdentifier} already queued." });
                                return; // Skip API call
                            }

                            // --- Steam API Call ---
                            cancellationToken.ThrowIfCancellationRequested();
                            SteamApiResponse? apiResponse = null;
                            try
                            {
                                apiResponse = await _steamApiClient.GetFileDetailsAsync(currentSteamId, cancellationToken);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception apiEx)
                            {
                                var errorMsg = $"Network/API error for {modIdentifier}: {apiEx.Message}";
                                _logger.LogWarning(errorMsg, nameof(SteamWorkshopQueueProcessor));
                                errorMessagesBag.Add(errorMsg);
                                Interlocked.Increment(ref failedProcessingCount);
                                progress?.Report(new QueueProcessProgress { /*...*/ Message = $"API Error for {modIdentifier}" });
                                return;
                            }

                            cancellationToken.ThrowIfCancellationRequested();

                            // --- Validate API Response ---
                            if (apiResponse?.Response?.PublishedFileDetails == null || !apiResponse.Response.PublishedFileDetails.Any())
                            {
                                var errorMsg = $"No details returned from Steam API for {modIdentifier}.";
                                _logger.LogWarning(errorMsg, nameof(SteamWorkshopQueueProcessor));
                                errorMessagesBag.Add(errorMsg);
                                Interlocked.Increment(ref failedProcessingCount);
                                progress?.Report(new QueueProcessProgress { /*...*/ Message = $"No Details for {modIdentifier}" });
                                return;
                            }

                            var details = apiResponse.Response.PublishedFileDetails.First();
                            modIdentifier = $"'{details.Title ?? currentSteamId}' ({currentSteamId})"; // Update identifier
                            modNameForProgress = details.Title ?? "Unknown"; // Update name

                            if (details.Result != 1) // 1 = OK
                            {
                                string errorDescription = SteamApiResultHelper.GetDescription(details.Result);
                                var errorMsg = $"Steam API error for {modIdentifier}: {errorDescription} (Code: {details.Result})";
                                _logger.LogWarning(errorMsg, nameof(SteamWorkshopQueueProcessor));
                                errorMessagesBag.Add(errorMsg);
                                Interlocked.Increment(ref failedProcessingCount);
                                progress?.Report(new QueueProcessProgress { /*...*/ CurrentItemName = modNameForProgress, Message = $"API Error ({details.Result}) for {modNameForProgress}" });
                                return;
                            }

                            // --- Create ModInfoDto ---
                            DateTimeOffset apiUpdateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated);
                            DateTime apiUpdateTimeUtc = apiUpdateTimeOffset.UtcDateTime;

                            var modInfoDto = new ModInfoDto
                            {
                                Name = details.Title ?? $"Unknown Mod {currentSteamId}",
                                SteamId = currentSteamId,
                                Url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={currentSteamId}",
                                PublishDate = apiUpdateTimeOffset.ToString("d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture),
                                StandardDate = apiUpdateTimeUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                                FileSize = details.FileSize,
                                LatestVersions = SteamApiResultHelper.ExtractAndSortVersionTags(details.Tags) // <-- THIS LINE WAS MISSING
                            };

                             // --- Report Before Queueing ---
                            progress?.Report(new QueueProcessProgress
                            {
                                CurrentItem = currentItemIndex,
                                TotalItems = result.TotalAttempted,
                                CurrentSteamId = currentSteamId,
                                CurrentItemName = modNameForProgress,
                                Message = $"Queueing {modNameForProgress}..."
                            });


                            // --- Add to Queue ---
                            if (_downloadQueueService.AddToQueue(modInfoDto))
                            {
                                Interlocked.Increment(ref successfullyAddedCount);
                                addedNamesBag.Add(modInfoDto.Name);
                                _logger.LogInfo($"Added {modIdentifier} to download queue.", nameof(SteamWorkshopQueueProcessor));
                                progress?.Report(new QueueProcessProgress { /*...*/ CurrentItemName = modNameForProgress, Message = $"Queued {modNameForProgress}" });
                            }
                            else
                            {
                                // AddToQueue likely returned false because it was added between the check and now (race condition)
                                // OR because of an internal queue error. Assume it's likely a race condition handled by the earlier check.
                                // We could re-check IsInQueue here, but for simplicity, treat AddToQueue false as a skip/already-queued for counts.
                                Interlocked.Increment(ref alreadyQueuedCount);
                                _logger.LogWarning($"AddToQueue returned false for {modIdentifier}. Likely already added or internal issue.", nameof(SteamWorkshopQueueProcessor));
                                // Optionally report this as a specific 'skipped' state in progress/results if needed
                                progress?.Report(new QueueProcessProgress { /*...*/ CurrentItemName = modNameForProgress, Message = $"Skipped/Already Queued {modNameForProgress}" });
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInfo($"Task cancelled for mod {modIdentifier}.", nameof(SteamWorkshopQueueProcessor));
                            // Let the outer catch handle setting WasCancelled flag
                            throw; // Re-throw to be caught by Task.WhenAll
                        }
                        catch (Exception taskEx)
                        {
                            var errorMsg = $"Unexpected error processing {modIdentifier}: {taskEx.Message}";
                            _logger.LogException(taskEx, errorMsg, nameof(SteamWorkshopQueueProcessor));
                            errorMessagesBag.Add(errorMsg);
                            Interlocked.Increment(ref failedProcessingCount); // Count unexpected errors as failures
                            progress?.Report(new QueueProcessProgress { /*...*/ CurrentItemName = modNameForProgress, Message = $"Unexpected Error for {modNameForProgress}" });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken)); // Pass token to Task.Run
                } // --- End ForEach Loop ---

                // Asynchronously wait for all tasks to complete
                await Task.WhenAll(tasks);

            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Steam API check and queue process was cancelled.", nameof(SteamWorkshopQueueProcessor));
                result.WasCancelled = true;
                // Add a generic cancellation message if desired, or let the caller handle it
                // errorMessagesBag.Add("Operation cancelled by user.");
            }
            catch (Exception ex) // Catch unexpected errors during setup or Task.WhenAll itself
            {
                _logger.LogException(ex, "Outer error during Steam API check/queue processing.", nameof(SteamWorkshopQueueProcessor));
                // Record a general error
                errorMessagesBag.Add($"An unexpected error occurred during processing: {ex.Message}");
                // If this happens, counts might be inaccurate, but record what we have.
                result.FailedProcessing = failedProcessingCount + (result.TotalAttempted - processedCount); // Estimate failures
            }
            finally
            {
                // Populate final results from counters and bags
                result.SuccessfullyAdded = successfullyAddedCount;
                result.AlreadyQueued = alreadyQueuedCount;
                // Make sure failed count reflects reality if Task.WhenAll threw early
                result.FailedProcessing = Math.Max(failedProcessingCount, result.TotalAttempted - successfullyAddedCount - alreadyQueuedCount);
                result.ErrorMessages.AddRange(errorMessagesBag);
                result.AddedModNames.AddRange(addedNamesBag);

                _logger.LogInfo($"Steam API check/queue process finished. Added: {result.SuccessfullyAdded}, AlreadyQueued: {result.AlreadyQueued}, Failed: {result.FailedProcessing}, Cancelled: {result.WasCancelled}", nameof(SteamWorkshopQueueProcessor));
            }

            return result;
        }
    }
}