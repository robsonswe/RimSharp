using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Shared.Services.Implementations
{
    public class UpdaterService : IUpdaterService
    {
        private readonly HttpClient _httpClient;
        private readonly IPathService _pathService;
        private readonly ILoggerService _logger;
        private const string GogApiUrl = "https://content-system.gog.com/products/1094900565/os/windows/builds?generation=2";

        public UpdaterService(IHttpClientFactory httpClientFactory, IPathService pathService, ILoggerService logger)
        {
            _httpClient = httpClientFactory.CreateClient("GOG_Updater");
            _pathService = pathService;
            _logger = logger;
        }

        public async Task<(bool isUpdateAvailable, string? latestVersion)> CheckForUpdateAsync()
        {
            string currentVersionString = _pathService.GetGameVersion();
            if (currentVersionString.StartsWith("N/A"))
            {
                _logger.LogWarning("Cannot check for updates because current game version is not available.", "UpdaterService");
                return (false, null);
            }

            try
            {
                _logger.LogInfo("Checking for RimWorld updates from GOG API.", "UpdaterService");
                using var responseStream = await _httpClient.GetStreamAsync(GogApiUrl);

                // <<< CHANGE START: Deserialize to the new wrapper object <<<
                var apiResponse = await JsonSerializer.DeserializeAsync<GogApiResponse>(responseStream);

                // Now, access the list of builds from the 'Items' property
                var latestPublicBuild = apiResponse?.Items?
                // >>> CHANGE END >>>
                    .Where(b => b.IsPublic && !string.IsNullOrEmpty(b.VersionName))
                    .Select(b => new { Build = b, Version = ParseVersion(b.VersionName) })
                    .Where(x => x.Version != null)
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault();

                if (latestPublicBuild == null)
                {
                    _logger.LogWarning("No valid public builds found in GOG API response.", "UpdaterService");
                    return (false, null);
                }

                string latestVersionString = latestPublicBuild.Build.VersionName!;
                Version? latestVersion = latestPublicBuild.Version;
                Version? currentVersion = ParseVersion(currentVersionString);

                if (currentVersion == null)
                {
                    _logger.LogWarning($"Could not parse current version for comparison: '{currentVersionString}'", "UpdaterService");
                    return (false, null);
                }

                if (latestVersion > currentVersion)
                {
                    _logger.LogInfo($"Update available. Current: {currentVersion}, Latest: {latestVersion} ('{latestVersionString}')", "UpdaterService");
                    return (true, latestVersionString);
                }

                _logger.LogInfo($"Game is up to date. Current: {currentVersion}, Latest: {latestVersion}", "UpdaterService");
                return (false, null);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Failed to check for updates from GOG API.", "UpdaterService");
                return (false, null);
            }
        }

        private Version? ParseVersion(string? versionString)
        {
            if (string.IsNullOrEmpty(versionString)) return null;

            var span = versionString.AsSpan();
            var spaceIndex = span.IndexOf(' ');
            if (spaceIndex > -1)
            {
                span = span.Slice(0, spaceIndex);
            }

            if (Version.TryParse(span, out var version))
            {
                return version;
            }
            return null;
        }
    }
}