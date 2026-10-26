using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Data
{
    public class DataUpdateService : IDataUpdateService
    {
        private readonly ILoggerService _logger;
        private readonly HttpClient _httpClient;
        private readonly string _localRulesPath;
        private readonly string _bundledRulesSource;
        private readonly string _remoteManifestUrl = "https://raw.githubusercontent.com/robsonswe/RimSharpDB/main/manifest.json";
        private readonly string _remoteBaseUrl = "https://raw.githubusercontent.com/robsonswe/RimSharpDB/main/";

        public DataUpdateService(ILoggerService logger, string appBasePath, HttpClient httpClient)
            : this(logger, appBasePath, httpClient, Path.Combine(AppDataPaths.BaseDirectory, "Rules", "db"))
        {
        }

        internal DataUpdateService(ILoggerService logger, string appBasePath, HttpClient httpClient, string bundledRulesSource)
        {
            _logger = logger;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _bundledRulesSource = bundledRulesSource;

            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "RimSharp-App");
            }

            // Store data in a reliable, user-specific location
            _localRulesPath = Path.Combine(appBasePath, "Rules", "db");

            SeedFromBundledPlaceholders();

            Directory.CreateDirectory(_localRulesPath);
        }

        /// <summary>
        /// First-run seeding: when the writable Rules/db does not exist yet
        /// (e.g. fresh XDG data directory on Linux), copy the bundled
        /// placeholder files that ship next to the executable
        /// ("&lt;BaseDirectory&gt;/Rules/db") into the writable location.
        /// On Windows the writable location IS the exe-side directory, so
        /// this is a no-op there.
        /// </summary>
        private void SeedFromBundledPlaceholders()
        {
            try
            {
                if (Directory.Exists(_localRulesPath) && Directory.EnumerateFiles(_localRulesPath).Any())
                {
                    return; // Already populated.
                }

                var bundledDir = _bundledRulesSource;
                if (!Directory.Exists(bundledDir))
                {
                    return;
                }

                // Never copy a directory onto itself (Windows case).
                if (string.Equals(
                        Path.GetFullPath(bundledDir),
                        Path.GetFullPath(_localRulesPath),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return;
                }

                Directory.CreateDirectory(_localRulesPath);
                foreach (var file in Directory.GetFiles(bundledDir))
                {
                    File.Copy(file, Path.Combine(_localRulesPath, Path.GetFileName(file)), overwrite: false);
                }

                _logger.LogInfo($"Seeded Rules/db from bundled placeholders: {bundledDir} -> {_localRulesPath}", nameof(DataUpdateService));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to seed Rules/db from bundled placeholders: {ex.Message}", nameof(DataUpdateService));
            }
        }

        public string GetDataFilePath(string fileName)
        {
            return Path.Combine(_localRulesPath, fileName);
        }

        public async Task CheckForAndApplyUpdatesAsync(IProgress<DataUpdateProgress> progress, CancellationToken cancellationToken)
        {
            _logger.LogInfo("Checking for data file updates...", nameof(DataUpdateService));
            progress.Report(new DataUpdateProgress { Message = "Contacting server...", Percentage = 0 });

            try
            {
                // 1. Download the remote manifest
                var remoteManifestJson = await _httpClient.GetStringAsync(_remoteManifestUrl, cancellationToken)
                    .ConfigureAwait(false);

                var remoteManifest = JsonDocument.Parse(remoteManifestJson);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new DataUpdateProgress { Message = "Comparing versions...", Percentage = 20 });
                string localManifestPath = GetDataFilePath("manifest.json");
                if (File.Exists(localManifestPath))
                {
                    var localManifestJson = await File.ReadAllTextAsync(localManifestPath, cancellationToken)
                        .ConfigureAwait(false);

                    if (localManifestJson == remoteManifestJson)
                    {
                        _logger.LogInfo("Data files are up to date.", nameof(DataUpdateService));
                        progress.Report(new DataUpdateProgress { Message = "Already up to date!", Percentage = 100 });
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                // 3. Download files listed in the manifest
                var filesToDownload = remoteManifest.RootElement.GetProperty("files").EnumerateObject().ToList();
                int fileCount = filesToDownload.Count;
                int filesDownloaded = 0;

                foreach (var fileProperty in filesToDownload)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath = fileProperty.Value.GetProperty("path").GetString() ?? throw new InvalidOperationException("Invalid manifest: file path missing.");
                    string fileName = Path.GetFileName(relativePath);

                    int currentProgress = 30 + (int)((double)filesDownloaded / fileCount * 60);
                    progress.Report(new DataUpdateProgress { Message = $"Downloading {fileName}...", Percentage = currentProgress });

                    string remoteFileUrl = _remoteBaseUrl + relativePath;
                    var fileContent = await _httpClient.GetStringAsync(remoteFileUrl, cancellationToken)
                        .ConfigureAwait(false);

                    await File.WriteAllTextAsync(GetDataFilePath(fileName), fileContent, cancellationToken)
                        .ConfigureAwait(false);
                    filesDownloaded++;
                }
                progress.Report(new DataUpdateProgress { Message = "Finalizing...", Percentage = 95 });
                await File.WriteAllTextAsync(localManifestPath, remoteManifestJson, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInfo("Data files updated successfully.", nameof(DataUpdateService));
                progress.Report(new DataUpdateProgress { Message = "Update Complete!", Percentage = 100 });
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Data update operation was cancelled.", nameof(DataUpdateService));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Failed to update data files. Using cached version.", nameof(DataUpdateService));
                throw new InvalidOperationException("Failed to download rule updates. Please check your internet connection.", ex);
            }
        }
    }
}


