#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;
using System.Diagnostics;

namespace RimSharp.Shared.Services.Implementations
{
    public class AppUpdaterService : IAppUpdaterService
    {
        private readonly HttpClient _httpClient;
        private readonly ILoggerService _logger;
        private const string GitHubApiUrl = "https://api.github.com/repos/robsonswe/RimSharp/releases/latest";

        public AppUpdaterService(IHttpClientFactory httpClientFactory, ILoggerService logger)
        {
            _httpClient = httpClientFactory.CreateClient("GitHub_Updater");
            // GitHub API requires a User-Agent header
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "RimSharp-Updater");
            }
            _logger = logger;
        }

        public async Task<(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl)> CheckForAppUpdateAsync()
        {
            try
            {
                _logger.LogInfo("Checking for RimSharp updates from GitHub API.", "AppUpdaterService");
                
                using var response = await _httpClient.GetAsync(GitHubApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"GitHub API returned status code: {response.StatusCode}", "AppUpdaterService");
                    return (false, null, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var releaseInfo = JsonSerializer.Deserialize<GitHubReleaseInfo>(json);

                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.TagName))
                {
                    _logger.LogWarning("Could not parse GitHub release info.", "AppUpdaterService");
                    return (false, null, null);
                }

                string latestTagName = releaseInfo.TagName;
                Version? latestVersion = ParseVersion(latestTagName);
                Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                if (latestVersion == null || currentVersion == null)
                {
                    _logger.LogWarning($"Could not parse versions for comparison. Latest: {latestTagName}, Current: {currentVersion}", "AppUpdaterService");
                    return (false, latestTagName, releaseInfo.HtmlUrl);
                }

                // Standard Version comparison: 1.11.0 > 1.9.0
                if (latestVersion > currentVersion)
                {
                    _logger.LogInfo($"App update available. Current: {currentVersion}, Latest: {latestVersion}", "AppUpdaterService");
                    return (true, latestTagName, releaseInfo.HtmlUrl);
                }

                _logger.LogInfo($"App is up to date. Current: {currentVersion}, Latest: {latestVersion}", "AppUpdaterService");
                return (false, latestTagName, releaseInfo.HtmlUrl);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Failed to check for app updates from GitHub.", "AppUpdaterService");
                return (false, null, null);
            }
        }

        private Version? ParseVersion(string versionString)
        {
            if (string.IsNullOrEmpty(versionString)) return null;

            // Remove 'v' prefix if present
            string cleanVersion = versionString.TrimStart('v', 'V');
            
            // Handle pre-release tags (e.g., 1.2.3-beta) by taking only the numeric part
            int dashIndex = cleanVersion.IndexOf('-');
            if (dashIndex > -1)
            {
                cleanVersion = cleanVersion.Substring(0, dashIndex);
            }

            if (Version.TryParse(cleanVersion, out var version))
            {
                return version;
            }
            return null;
        }

        private class GitHubReleaseInfo
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }
        }
    }
}
