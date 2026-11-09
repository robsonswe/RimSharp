using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;

namespace RimSharp.Services.Workshop
{
    /// <summary>
    /// Client for Steam Workshop API.
    /// Based on the researched community approach for Steam RemoteStorage API.
    ///
    /// Behavior (per §5.4 / 1.4 hardening):
    /// - Batches published file ID lookups into chunks of MaxBatchSize (300, the Steam API limit).
    /// - Retries each chunk with exponential backoff (1s, 2s, then give up — 3 attempts) on
    ///   transient failures: HTTP 429/500/502/503/504, timeouts, and connection errors.
    /// - On terminal failure (all attempts exhausted), returns an empty list — the update
    ///   service maps that to per-mod failure outcomes.
    /// </summary>
    public class SteamWorkshopApiClient
    {
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const int MaxBatchSize = 300; // Steam API limit
        private const int MaxAttempts = 3;
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// HTTP status codes that are considered transient and therefore retryable
        /// (based on the researched community approach: 429, 500, 502, 503, 504).
        /// </summary>
        private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
        {
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout,
        };

        public SteamWorkshopApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Get details for a single workshop mod.
        /// </summary>
        public async Task<WorkshopModDetails?> GetModDetailsAsync(string steamId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                return null;
            }

            var modDetails = await GetModDetailsBatchAsync(new[] { steamId }, cancellationToken);
            var details = modDetails.FirstOrDefault(d => d.PublishedFileId == steamId);
            return details?.Result == 1 ? details : null;
        }

        /// <summary>
        /// Get details for multiple workshop mods.
        /// </summary>
        public async Task<List<WorkshopModDetails>> GetModDetailsBatchAsync(IEnumerable<string> steamIds, CancellationToken cancellationToken = default)
        {
            var results = new List<WorkshopModDetails>();
            var idList = steamIds.ToList();

            // Process in batches due to API limits
            for (int i = 0; i < idList.Count; i += MaxBatchSize)
            {
                var batch = idList.Skip(i).Take(MaxBatchSize).ToList();
                var batchResults = await GetModDetailsBatchInternalAsync(batch, cancellationToken);
                results.AddRange(batchResults);
            }

            return results;
        }

        private async Task<List<WorkshopModDetails>> GetModDetailsBatchInternalAsync(List<string> steamIds, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(AttemptTimeout);

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl);
                    var formData = new Dictionary<string, string>
                    {
                        { "itemcount", steamIds.Count.ToString() }
                    };

                    for (int i = 0; i < steamIds.Count; i++)
                    {
                        formData.Add($"publishedfileids[{i}]", steamIds[i]);
                    }

                    using var formContent = new FormUrlEncodedContent(formData);
                    request.Content = formContent;

                    using HttpResponseMessage response = await _httpClient.SendAsync(request, attemptCts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (RetryableStatusCodes.Contains(response.StatusCode) && attempt < MaxAttempts)
                        {
                            await Task.Delay((int)Math.Pow(2, attempt - 1) * 1000, cancellationToken); // 1s, 2s, ...
                            continue;
                        }

                        // Terminal failure (or non-retryable status): collapse to empty list.
                        return new List<WorkshopModDetails>();
                    }

                    string jsonResponse = await response.Content.ReadAsStringAsync(attemptCts.Token);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString
                    };

                    var apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                    if (apiResponse?.Response?.PublishedFileDetails == null)
                    {
                        // Malformed / failed payload → retry if attempts remain.
                        if (attempt < MaxAttempts)
                        {
                            await Task.Delay((int)Math.Pow(2, attempt - 1) * 1000, cancellationToken);
                            continue;
                        }

                        return new List<WorkshopModDetails>();
                    }

                    return apiResponse.Response.PublishedFileDetails ?? new List<WorkshopModDetails>();
                }
                catch (OperationCanceledException) when (!attemptCts.IsCancellationRequested)
                {
                    // The cancellation did not come from this attempt's timeout token:
                    // it was driven by the caller's token (user cancellation). Propagate.
                    throw;
                }
                catch (Exception)
                {
                    // Connection errors, timeouts, parse failures, etc. — retry with backoff if attempts remain.
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay((int)Math.Pow(2, attempt - 1) * 1000, cancellationToken);
                        continue;
                    }

                    // Give up → whole-chunk failure.
                    return new List<WorkshopModDetails>();
                }
            }

            // Should not be reached, but return an empty chunk to satisfy the contract.
            return new List<WorkshopModDetails>();
        }
    }

    /// <summary>
    /// Steam API response wrapper.
    /// </summary>
    public class SteamApiResponse
    {
        [JsonPropertyName("response")]
        public SteamApiResponseData? Response { get; set; }
    }

    /// <summary>
    /// Steam API response data.
    /// </summary>
    public class SteamApiResponseData
    {
        [JsonPropertyName("publishedfiledetails")]
        public List<WorkshopModDetails>? PublishedFileDetails { get; set; }

        [JsonPropertyName("result")]
        public int Result { get; set; }
    }
}