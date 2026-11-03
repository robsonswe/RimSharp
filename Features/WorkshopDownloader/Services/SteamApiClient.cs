#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Diagnostics;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class SteamApiClient : ISteamApiClient
    {
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        public SteamApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<SteamApiResponse?> GetFileDetailsAsync(string steamId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                Debug.WriteLine($"[SteamApiClient] Invalid Steam ID provided: '{steamId}'");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

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
                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                Debug.WriteLine($"[SteamApiClient] Received API response for ID {steamId}: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    Debug.WriteLine($"[SteamApiClient] Request failed for ID {steamId}: {response.StatusCode}. Content: {errorContent}");
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();
                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
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
        }

        public async Task<SteamCollectionDetailsResponse?> GetCollectionDetailsAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionId) || !long.TryParse(collectionId, out _))
            {
                Debug.WriteLine($"[SteamApiClient] Invalid Collection ID provided: '{collectionId}'");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

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

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<SteamCollectionDetailsResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamApiClient] Error fetching collection {collectionId}: {ex.Message}");
                return null;
            }
        }

        public async Task<SteamApiResponse?> GetPublishedFileDetailsBatchAsync(IEnumerable<string> fileIds, CancellationToken cancellationToken = default)
        {
            var idList = fileIds.ToList();
            if (!idList.Any()) return null;

            cancellationToken.ThrowIfCancellationRequested();

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

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
                return JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamApiClient] Error fetching batch published file details: {ex.Message}");
                return null;
            }
        }
        public async Task<string?> GetPersonaNameAsync(string steamId64, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId64) || !long.TryParse(steamId64, out _))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Public profile XML endpoint — no Web API key required.
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://steamcommunity.com/profiles/{steamId64}/?xml=1");
                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[SteamApiClient] Persona lookup failed for {steamId64}: {response.StatusCode}");
                    return null;
                }

                string xml = await response.Content.ReadAsStringAsync(cancellationToken);
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
        }
    }
}

