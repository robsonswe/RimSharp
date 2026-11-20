#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class SteamApiClient : ISteamApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly IApiResponseCache _cache;
        private const string ApiBaseUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const int MaxBatchSize = 300; // Steam API limit
        private const int MaxAttempts = 3;
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// HTTP status codes that are considered transient and therefore retryable
        /// (429, 500, 502, 503, 504).
        /// </summary>
        private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
        {
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout,
        };

        public SteamApiClient(HttpClient httpClient, IApiResponseCache cache)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<SteamApiResponse?> GetFileDetailsAsync(string steamId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                Debug.WriteLine($"[SteamApiClient] Invalid Steam ID provided: '{steamId}'");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return await _cache.GetOrFetchAsync<SteamApiResponse>(
                $"steam:file:{steamId}",
                TimeSpan.FromHours(12),
                async ct =>
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl);
                        var formData = new Dictionary<string, string>
                        {
                            { "itemcount", "1" },
                            { $"publishedfileids[0]", steamId }
                        };
                        using var formContent = new FormUrlEncodedContent(formData);
                        request.Content = formContent;

                        Debug.WriteLine($"[SteamApiClient] Sending API request for ID: {steamId}");
                        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                        Debug.WriteLine($"[SteamApiClient] Received API response for ID {steamId}: {response.StatusCode}");

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorContent = await response.Content.ReadAsStringAsync(ct);
                            Debug.WriteLine($"[SteamApiClient] Request failed for ID {steamId}: {response.StatusCode}. Content: {errorContent}");
                            return null;
                        }

                        ct.ThrowIfCancellationRequested();
                        string jsonResponse = await response.Content.ReadAsStringAsync(ct);
                        // Configure JsonSerializerOptions to handle numbers represented as strings
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,

                            NumberHandling = JsonNumberHandling.AllowReadingFromString
                        };

                        // Deserialize using the configured options
                        SteamApiResponse? apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                        if (apiResponse?.Response?.PublishedFileDetails == null || apiResponse.Response.Result != 1)
                        {
                            Debug.WriteLine($"[SteamApiClient] Response structure invalid or indicates failure for ID {steamId}. Result Code: {apiResponse?.Response?.Result}. Raw JSON: {jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500))}");
                            return null;
                        }

                        return apiResponse;
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine($"[SteamApiClient] Operation cancelled for Steam API ID {steamId}.");
                        return null;
                    }
                    catch (JsonException jsonEx) // Catch deserialization errors
                    {
                        Debug.WriteLine($"[SteamApiClient] Error deserializing response for ID {steamId}: {jsonEx.Message}");
                        return null;
                    }
                    catch (HttpRequestException httpEx)
                    {
                        Debug.WriteLine($"[SteamApiClient] HTTP request error for ID {steamId}: {httpEx.Message}");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamApiClient] Unexpected error calling Steam API for ID {steamId}: {ex}");
                        return null;
                    }
                },
                cancellationToken);
        }

        public async Task<SteamCollectionDetailsResponse?> GetCollectionDetailsAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionId) || !long.TryParse(collectionId, out _))
            {
                Debug.WriteLine($"[SteamApiClient] Invalid Collection ID provided: '{collectionId}'");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return await _cache.GetOrFetchAsync<SteamCollectionDetailsResponse>(
                $"steam:collection:{collectionId}",
                TimeSpan.FromHours(24),
                async ct =>
                {
                    try
                    {
                        const string url = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";
                        using var request = new HttpRequestMessage(HttpMethod.Post, url);
                        var formData = new Dictionary<string, string>
                        {
                            { "collectioncount", "1" },
                            { $"publishedfileids[0]", collectionId }
                        };
                        using var formContent = new FormUrlEncodedContent(formData);
                        request.Content = formContent;

                        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                        if (!response.IsSuccessStatusCode) return null;

                        string jsonResponse = await response.Content.ReadAsStringAsync(ct);
                        return JsonSerializer.Deserialize<SteamCollectionDetailsResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamApiClient] Error fetching collection {collectionId}: {ex.Message}");
                        return null;
                    }
                },
                cancellationToken);
        }

        public async Task<SteamApiResponse?> GetPublishedFileDetailsBatchAsync(IEnumerable<string> fileIds, CancellationToken cancellationToken = default)
        {
            var idList = fileIds.ToList();
            if (!idList.Any()) return null;

            cancellationToken.ThrowIfCancellationRequested();

            // Order-independent key so the same set of IDs shares one entry.
            var keyIds = string.Join(",", idList.OrderBy(id => id, StringComparer.Ordinal));
            var key = $"steam:files-batch:{ComputeSha256(keyIds)}";

            return await _cache.GetOrFetchAsync<SteamApiResponse>(
                key,
                TimeSpan.FromHours(12),
                async ct =>
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl);
                        var formData = new Dictionary<string, string>
                        {
                            { "itemcount", idList.Count.ToString() }
                        };
                        for (int i = 0; i < idList.Count; i++)
                        {
                            formData.Add($"publishedfileids[{i}]", idList[i]);
                        }
                        using var formContent = new FormUrlEncodedContent(formData);
                        request.Content = formContent;

                        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                        if (!response.IsSuccessStatusCode) return null;

                        string jsonResponse = await response.Content.ReadAsStringAsync(ct);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
                        var apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                        // Mirror GetFileDetailsAsync: a failed or empty payload must
                        // never be cached as a success. Returning null lets the cache's
                        // null→stale fallback apply so callers can refetch later.
                        if (apiResponse?.Response?.PublishedFileDetails == null ||
                            apiResponse.Response.Result != 1 ||
                            apiResponse.Response.PublishedFileDetails.Count == 0)
                        {
                            Debug.WriteLine($"[SteamApiClient] Batch response invalid or empty for IDs [{string.Join(",", idList)}]. Result Code: {apiResponse?.Response?.Result}. Raw JSON: {jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500))}");
                            return null;
                        }

                        return apiResponse;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamApiClient] Error fetching batch published file details: {ex.Message}");
                        return null;
                    }
                },
                cancellationToken);
        }

        public async Task<string?> GetPersonaNameAsync(string steamId64, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId64) || !long.TryParse(steamId64, out _))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return await _cache.GetOrFetchAsync<string>(
                $"steam:persona:{steamId64}",
                TimeSpan.FromDays(30),
                async ct =>
                {
                    try
                    {
                        // Public profile XML endpoint — no Web API key required.
                        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://steamcommunity.com/profiles/{steamId64}/?xml=1");
                        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[SteamApiClient] Persona lookup failed for {steamId64}: {response.StatusCode}");
                            return null;
                        }

                        string xml = await response.Content.ReadAsStringAsync(ct);
                        const string OpenTag = "<steamID>";
                        const string CloseTag = "</steamID>";
                        int start = xml.IndexOf(OpenTag, StringComparison.OrdinalIgnoreCase);
                        if (start < 0) return null;
                        start += OpenTag.Length;
                        int end = xml.IndexOf(CloseTag, start, StringComparison.OrdinalIgnoreCase);
                        if (end < 0) return null;

                        var name = System.Net.WebUtility.HtmlDecode(xml.Substring(start, end - start)).Trim();
                        // Strip CDATA wrapper: <steamID><![CDATA[Author]]></steamID>
                        if (name.StartsWith("<![CDATA[", StringComparison.Ordinal))
                        {
                            name = name.Substring(9);
                            var cdataEnd = name.IndexOf("]]>", StringComparison.Ordinal);
                            name = cdataEnd >= 0 ? name.Substring(0, cdataEnd).Trim() : name.Trim();
                        }
                        return string.IsNullOrWhiteSpace(name) ? null : name;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SteamApiClient] Persona lookup error for {steamId64}: {ex.Message}");
                        return null;
                    }
                },
                cancellationToken);
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
        /// Get details for multiple workshop mods. Chunks into batches of 300 (the
        /// Steam API limit) and retries each chunk with exponential backoff on
        /// transient failures. Terminal failure yields an empty list.
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
            // Order-independent key so the same set of IDs shares one entry.
            var keyIds = string.Join(",", steamIds.OrderBy(id => id, StringComparer.Ordinal));
            var key = $"steam:workshop-details:{ComputeSha256(keyIds)}";

            // The fetch delegate returns null for any failure or empty payload so
            // the cache never stores an empty/failed result. The cache's stale
            // fallback then covers transient failures; null is mapped back to an
            // empty list below, preserving the terminal-failure contract.
            var result = await _cache.GetOrFetchAsync<List<WorkshopModDetails>>(
                key,
                TimeSpan.FromHours(1),
                async ct =>
                {
                    for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                    {
                        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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
                                    await Task.Delay((int)Math.Pow(2, attempt - 1) * 1000, ct); // 1s, 2s, ...
                                    continue;
                                }

                                // Terminal failure (or non-retryable status): collapse to empty list.
                                return null;
                            }

                            string jsonResponse = await response.Content.ReadAsStringAsync(attemptCts.Token);
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                NumberHandling = JsonNumberHandling.AllowReadingFromString
                            };

                            var apiResponse = JsonSerializer.Deserialize<WorkshopApiResponse>(jsonResponse, options);

                            if (apiResponse?.Response?.PublishedFileDetails == null)
                            {
                                // Malformed / failed payload → retry if attempts remain.
                                if (attempt < MaxAttempts)
                                {
                                    await Task.Delay((int)Math.Pow(2, attempt - 1) * 1000, ct);
                                    continue;
                                }

                                return null;
                            }

                            var details = apiResponse.Response.PublishedFileDetails;
                            // Empty payload means failure — never cache it.
                            return details.Count > 0 ? details : null;
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
                                await Task.Delay((int)Math.Pow(2, attempt - 1) * 1000, ct);
                                continue;
                            }

                            // Give up → whole-chunk failure.
                            return null;
                        }
                    }

                    // Should not be reached, but signal failure to satisfy the contract.
                    return null;
                },
                cancellationToken);

            return result ?? new List<WorkshopModDetails>();
        }

        private static string ComputeSha256(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Steam API response wrapper for the WorkshopModDetails path. Kept local to
    /// this client (distinct from the downloader's SteamApiResponse model) so the
    /// consumer_appid mapping on WorkshopModDetails is preserved.
    /// </summary>
    internal class WorkshopApiResponse
    {
        [JsonPropertyName("response")]
        public WorkshopApiResponseData? Response { get; set; }
    }

    /// <summary>
    /// Steam API response data for the WorkshopModDetails path.
    /// </summary>
    internal class WorkshopApiResponseData
    {
        [JsonPropertyName("publishedfiledetails")]
        public List<WorkshopModDetails>? PublishedFileDetails { get; set; }

        [JsonPropertyName("result")]
        public int Result { get; set; }
    }
}
