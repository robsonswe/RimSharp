using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Services
{
    public class SteamApiClient : ISteamApiClient
    {
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const string RimWorldAppId = "294100"; // For validation

        // Consider using IHttpClientFactory if your DI setup supports it for better HttpClient management
        public SteamApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<SteamApiResponse> GetFileDetailsAsync(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
            {
                Console.WriteLine($"Invalid Steam ID provided: {steamId}");
                return null; // Invalid ID format
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl);

                // Prepare form data
                var formData = new Dictionary<string, string>
                {
                    { "itemcount", "1" },
                    { $"publishedfileids[0]", steamId }
                };
                request.Content = new FormUrlEncodedContent(formData);

                // Set Headers (already done by FormUrlEncodedContent, but explicit can be clearer if needed elsewhere)
                // request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Steam API request failed for ID {steamId}: {response.StatusCode}");
                    // Optionally read error content: await response.Content.ReadAsStringAsync();
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Deserialize the JSON response
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true }; // Be flexible with casing just in case
                SteamApiResponse apiResponse = JsonSerializer.Deserialize<SteamApiResponse>(jsonResponse, options);

                // Basic validation of the response structure
                if (apiResponse?.Response?.PublishedFileDetails == null || apiResponse.Response.Result != 1)
                {
                     Console.WriteLine($"Steam API response structure invalid or indicates failure for ID {steamId}.");
                     // Log the raw response if needed for debugging: Console.WriteLine(jsonResponse);
                     return null;
                }

                // Optional: Validate App ID within the details if needed here,
                // or let the calling service handle it.
                // var details = apiResponse.Response.PublishedFileDetails.FirstOrDefault();
                // if (details != null && details.ConsumerAppId.ToString() != RimWorldAppId) { ... }

                return apiResponse;

            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"Error deserializing Steam API response for ID {steamId}: {jsonEx.Message}");
                return null;
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP request error for Steam API ID {steamId}: {httpEx.Message}");
                return null;
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                Console.WriteLine($"Unexpected error calling Steam API for ID {steamId}: {ex.Message}");
                return null;
            }
        }
    }
}
