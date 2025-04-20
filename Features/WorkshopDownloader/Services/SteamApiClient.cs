#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization; // Added for JsonNumberHandling
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

                // --- MODIFICATION START ---
                // Configure JsonSerializerOptions to handle numbers represented as strings
                var options = new JsonSerializerOptions
                {
                     PropertyNameCaseInsensitive = true,
                     // Allow deserializing JSON strings into C# number types (like long)
                     NumberHandling = JsonNumberHandling.AllowReadingFromString
                };
                // --- MODIFICATION END ---

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
                // Log the specific error, which helps diagnose problems like the one you encountered
                Debug.WriteLine($"[SteamApiClient] Error deserializing response for ID {steamId}: {jsonEx.Message}");
                // Optionally log the problematic JSON snippet if needed for deeper debugging
                // Debug.WriteLine($"[SteamApiClient] Problematic JSON snippet: {jsonResponse.Substring(Math.Max(0, jsonEx.BytePositionInLine - 50), 100)}");
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
    }
}
