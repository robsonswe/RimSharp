using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts; // For ILoggerService

public class DataUpdateService : IDataUpdateService
{
    private readonly ILoggerService _logger;
    private readonly HttpClient _httpClient;
    private readonly string _localRulesPath;
    private readonly string _remoteManifestUrl = "https://raw.githubusercontent.com/robsonswe/RimSharpDB/main/manifest.json";
    private readonly string _remoteBaseUrl = "https://raw.githubusercontent.com/robsonswe/RimSharpDB/main/";

    public DataUpdateService(ILoggerService logger, string appBasePath)

    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "RimSharp-App"); // GitHub requires a User-Agent

        // Store data in a reliable, user-specific location
        _localRulesPath = Path.Combine(appBasePath, "Rules", "db");

        Directory.CreateDirectory(_localRulesPath); 
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
                .ConfigureAwait(false); // <-- FIX HERE

            var remoteManifest = JsonDocument.Parse(remoteManifestJson);
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new DataUpdateProgress { Message = "Comparing versions...", Percentage = 20 });

            // 2. Load the local manifest and compare
            string localManifestPath = GetDataFilePath("manifest.json");
            if (File.Exists(localManifestPath))
            {
                var localManifestJson = await File.ReadAllTextAsync(localManifestPath, cancellationToken)
                    .ConfigureAwait(false); // <-- FIX HERE

                if (localManifestJson == remoteManifestJson)
                {
                    _logger.LogInfo("Data files are up to date.", nameof(DataUpdateService));
                    progress.Report(new DataUpdateProgress { Message = "Already up to date!", Percentage = 100 });
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false); // <-- FIX HERE
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
                string relativePath = fileProperty.Value.GetProperty("path").GetString();
                string fileName = Path.GetFileName(relativePath);

                int currentProgress = 30 + (int)((double)filesDownloaded / fileCount * 60);
                progress.Report(new DataUpdateProgress { Message = $"Downloading {fileName}...", Percentage = currentProgress });

                string remoteFileUrl = _remoteBaseUrl + relativePath;
                var fileContent = await _httpClient.GetStringAsync(remoteFileUrl, cancellationToken)
                    .ConfigureAwait(false); // <-- FIX HERE

                await File.WriteAllTextAsync(GetDataFilePath(fileName), fileContent, cancellationToken)
                    .ConfigureAwait(false); // <-- FIX HERE
                filesDownloaded++;
            }

            // 4. Save the new manifest locally
            progress.Report(new DataUpdateProgress { Message = "Finalizing...", Percentage = 95 });
            await File.WriteAllTextAsync(localManifestPath, remoteManifestJson, cancellationToken)
                .ConfigureAwait(false); // <-- FIX HERE

            _logger.LogInfo("Data files updated successfully.", nameof(DataUpdateService));
            progress.Report(new DataUpdateProgress { Message = "Update Complete!", Percentage = 100 });
            await Task.Delay(500, cancellationToken).ConfigureAwait(false); // <-- FIX HERE
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