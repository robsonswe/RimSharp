using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading; // Add this using
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;
using System.Diagnostics; // For Debug.WriteLine

#nullable enable // Enable nullable context for the implementation

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class SteamApiClient : ISteamApiClient
    {
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        // RimWorldAppId validation is handled by the caller (WorkshopUpdateCheckerService)

        // Consider using IHttpClientFactory if your DI setup supports it for better HttpClient management
        public SteamApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            // Optional: Configure default headers or timeout for the HttpClient instance here if not done elsewhere
            // _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // Implement the updated interface method
        public async Task<SteamApiResponse?> GetFileDetailsAsync(string steamId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                Debug.WriteLine($"[SteamApiClient] Invalid Steam ID provided: '{steamId}'");
                return null; // Invalid ID format
            }

            // Check for cancellation before even creating the request
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl);

                // Prepare form data
                var formData = new Dictionary<string, string>
                {
                    { "itemcount", "1" },
                    { $"publishedfileids[0]", steamId }
                };
                // Important: Dispose FormUrlEncodedContent when done
                using var formContent = new FormUrlEncodedContent(formData);
                request.Content = formContent;

                // Set Headers (FormUrlEncodedContent sets Content-Type automatically)

                Debug.WriteLine($"[SteamApiClient] Sending API request for ID: {steamId}");
                // Pass the cancellation token to SendAsync
                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                Debug.WriteLine($"[SteamApiClient] Received API response for ID {steamId}: {response.StatusCode}");


                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken); // Also use token here
                    Debug.WriteLine($"[SteamApiClient] Request failed for ID {steamId}: {response.StatusCode}. Content: {errorContent}");
                    return null;
                }

                // Check for cancellation after network request but before reading/parsing potentially large content
                cancellationToken.ThrowIfCancellationRequested();

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken); // Use token here

                // Deserialize the JSON response
                var options = new JsonSerializerOptions
                {
                     PropertyNameCaseInsensitive = true,
                     // Consider adding other options like AllowTrailingCommas if needed
                };
                SteamApiResponse? apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                // Basic validation of the response structure
                // Check if deserialization itself failed (apiResponse is null)
                // Check if the 'response' field within the JSON is missing
                // Check if the result code within the 'response' field indicates success (1)
                if (apiResponse?.Response?.PublishedFileDetails == null || apiResponse.Response.Result != 1)
                {
                     Debug.WriteLine($"[SteamApiClient] Response structure invalid or indicates failure for ID {steamId}. Result Code: {apiResponse?.Response?.Result}. Raw JSON: {jsonResponse.Substring(0, Math.Min(jsonResponse.Length, 500))}"); // Log snippet
                     return null;
                }

                // Caller (WorkshopUpdateCheckerService) handles App ID validation

                return apiResponse;

            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[SteamApiClient] Operation cancelled for Steam API ID {steamId}.");
                return null; // Return null when cancelled
            }
            catch (JsonException jsonEx)
            {
                Debug.WriteLine($"[SteamApiClient] Error deserializing response for ID {steamId}: {jsonEx.Message}");
                return null;
            }
            catch (HttpRequestException httpEx)
            {
                // This catches network-related errors (DNS, connection refused, etc.)
                 Debug.WriteLine($"[SteamApiClient] HTTP request error for ID {steamId}: {httpEx.Message}");
                return null;
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                Debug.WriteLine($"[SteamApiClient] Unexpected error calling Steam API for ID {steamId}: {ex}");
                // Consider logging the full exception details if using a proper logging framework
                return null;
            }
        }
    }
}