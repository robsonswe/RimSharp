using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Based on RimSort's ISteamRemoteStorage_GetPublishedFileDetails approach.
    /// </summary>
    public class SteamWorkshopApiClient
    {
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const int MaxBatchSize = 300; // Steam API limit

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

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                if (apiResponse?.Response?.PublishedFileDetails == null || apiResponse.Response.Result != 1)
                {
                    return null;
                }

                return apiResponse.Response.PublishedFileDetails[0];
            }
            catch (Exception)
            {
                return null;
            }
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

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    return new List<WorkshopModDetails>();
                }

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                if (apiResponse?.Response?.PublishedFileDetails == null)
                {
                    return new List<WorkshopModDetails>();
                }

                return apiResponse.Response.PublishedFileDetails;
            }
            catch (Exception)
            {
                return new List<WorkshopModDetails>();
            }
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

    /// <summary>
    /// Workshop mod details from Steam API.
    /// </summary>
    public class WorkshopModDetails
    {
        [JsonPropertyName("publishedfileid")]
        public string? PublishedFileId { get; set; }

        [JsonPropertyName("result")]
        public int Result { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("time_created")]
        public long? TimeCreated { get; set; }

        [JsonPropertyName("time_updated")]
        public long? TimeUpdated { get; set; }

        [JsonPropertyName("file_size")]
        public long? FileSize { get; set; }

        [JsonPropertyName("preview_url")]
        public string? PreviewUrl { get; set; }

        [JsonPropertyName("tags")]
        public List<WorkshopTag>? Tags { get; set; }

        [JsonPropertyName("children")]
        public List<WorkshopChild>? Children { get; set; }
    }

    /// <summary>
    /// Workshop tag information.
    /// </summary>
    public class WorkshopTag
    {
        [JsonPropertyName("tag")]
        public string? Tag { get; set; }
    }

    /// <summary>
    /// Workshop child/dependency information.
    /// </summary>
    public class WorkshopChild
    {
        [JsonPropertyName("publishedfileid")]
        public string? PublishedFileId { get; set; }
    }
}